"""Virtual keyboard for the test game's amdaemon.exe (no focus needed, real keyboard ignored).
Buttons, test/service and the Aime scan key are read by mai2hook.dll and hinata.dll inside amdaemon.exe
with user32!GetAsyncKeyState, and mai2hook only accepts them while GetForegroundWindow() is the game.
attach() puts two tiny x64 stubs into amdaemon and points those modules' import-table entries at them:
  GetAsyncKeyState(vk)  -> reads a 256-entry table we write from here
  GetForegroundWindow() -> returns the game window
so the game can sit off screen while tests press keys, and nothing typed on the real keyboard reaches it.
Only for the test instance; amdaemon is killed at the end of every test run."""
import ctypes, ctypes.wintypes as wt, os, struct, time
import pefile

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.VirtualAllocEx.restype = ctypes.c_void_p
k32.VirtualAllocEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wt.DWORD, wt.DWORD]
k32.WriteProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.VirtualProtectEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wt.DWORD, ctypes.POINTER(wt.DWORD)]
psapi.EnumProcessModulesEx.argtypes = [wt.HANDLE, ctypes.POINTER(ctypes.c_void_p), wt.DWORD, ctypes.POINTER(wt.DWORD), wt.DWORD]
psapi.GetModuleFileNameExW.argtypes = [wt.HANDLE, ctypes.c_void_p, wt.LPWSTR, wt.DWORD]

FG_STUB, KEY_STUB, HWND_SLOT, TABLE = 0x00, 0x20, 0x100, 0x200

class VirtualKeys:
    def __init__(self, pid, game_hwnd, modules=("mai2hook.dll", "hinata.dll")):
        self.h = k32.OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, False, pid)
        if not self.h: raise OSError("OpenProcess(amdaemon) failed: %d" % ctypes.get_last_error())
        self.mem = k32.VirtualAllocEx(self.h, None, 0x1000, 0x3000, 0x40)  # RWX
        if not self.mem: raise OSError("VirtualAllocEx failed: %d" % ctypes.get_last_error())
        # GetForegroundWindow: mov rax,[rip+d]; ret
        fg = b"\x48\x8B\x05" + struct.pack("<i", HWND_SLOT - (FG_STUB + 7)) + b"\xC3"
        # GetAsyncKeyState: movzx eax,cl; lea rdx,[rip+d]; movzx eax,word[rdx+rax*2]; ret
        ks = b"\x0F\xB6\xC1" + b"\x48\x8D\x15" + struct.pack("<i", TABLE - (KEY_STUB + 3 + 7)) + b"\x0F\xB7\x04\x42" + b"\xC3"
        self._write(FG_STUB, fg); self._write(KEY_STUB, ks)
        self._write(HWND_SLOT, struct.pack("<Q", game_hwnd or 0))
        self._write(TABLE, b"\0" * 512)
        bases = self._module_bases()
        self.patched = []
        for m in modules:
            base = bases.get(m.lower())
            if not base: continue
            pe = pefile.PE(os.path.join(os.path.dirname(self._exe_path()), m), fast_load=True)
            pe.parse_data_directories([pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"]])
            for imp in pe.DIRECTORY_ENTRY_IMPORT:
                if imp.dll.decode().lower() != "user32.dll": continue
                for s in imp.imports:
                    name = s.name.decode() if s.name else ""
                    target = {"GetAsyncKeyState": KEY_STUB, "GetForegroundWindow": FG_STUB}.get(name)
                    if target is None: continue
                    slot = base + (s.address - pe.OPTIONAL_HEADER.ImageBase)
                    self._patch_ptr(slot, self.mem + target)
                    self.patched.append("%s!%s" % (m, name))
        if not self.patched: raise OSError("no import entries patched (modules loaded: %s)" % sorted(bases))

    def _write(self, off, data, addr=None):
        n = ctypes.c_size_t()
        buf = ctypes.create_string_buffer(data, len(data))
        if not k32.WriteProcessMemory(self.h, addr if addr is not None else self.mem + off, buf, len(data), ctypes.byref(n)):
            raise OSError("WriteProcessMemory failed: %d" % ctypes.get_last_error())

    def _patch_ptr(self, slot, value):
        cur = ctypes.c_uint64(); n = ctypes.c_size_t()
        k32.ReadProcessMemory(self.h, slot, ctypes.byref(cur), 8, ctypes.byref(n))
        if not hasattr(self, "originals"): self.originals = []
        self.originals.append((slot, cur.value))
        old = wt.DWORD()
        k32.VirtualProtectEx(self.h, slot, 8, 0x04, ctypes.byref(old))
        self._write(0, struct.pack("<Q", value), addr=slot)
        k32.VirtualProtectEx(self.h, slot, 8, old.value, ctypes.byref(old))

    def _module_bases(self):
        arr = (ctypes.c_void_p * 1024)(); need = wt.DWORD()
        psapi.EnumProcessModulesEx(self.h, arr, ctypes.sizeof(arr), ctypes.byref(need), 3)
        out = {}
        for i in range(need.value // ctypes.sizeof(ctypes.c_void_p)):
            name = ctypes.create_unicode_buffer(520)
            psapi.GetModuleFileNameExW(self.h, arr[i], name, 520)
            out[os.path.basename(name.value).lower()] = arr[i]
        return out

    def _exe_path(self):
        name = ctypes.create_unicode_buffer(520)
        psapi.GetModuleFileNameExW(self.h, None, name, 520)
        return name.value

    def set_game_window(self, hwnd):
        self._write(HWND_SLOT, struct.pack("<Q", hwnd))

    def down(self, vk, is_down=True):
        self._write(TABLE + 2 * vk, struct.pack("<H", 0x8000 if is_down else 0))

    def press(self, vk, ms=150):
        self.down(vk, True); time.sleep(ms / 1000); self.down(vk, False)

    def restore(self):
        """Point the patched imports back at user32 so amdaemon reads the real keyboard again."""
        for slot, value in reversed(getattr(self, "originals", [])):
            old = wt.DWORD()
            k32.VirtualProtectEx(self.h, slot, 8, 0x04, ctypes.byref(old))
            self._write(0, struct.pack("<Q", value), addr=slot)
            k32.VirtualProtectEx(self.h, slot, 8, old.value, ctypes.byref(old))
        self.originals = []


def attached(pid, module="mai2hook.dll"):
    """Reuse the virtual keyboard another script already put into amdaemon (finds it via the patched
    GetAsyncKeyState import of mai2hook.dll); returns a VirtualKeys-like object sharing the same table."""
    self = VirtualKeys.__new__(VirtualKeys)
    self.h = k32.OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, False, pid)
    base = self._module_bases()[module]
    pe = pefile.PE(os.path.join(os.path.dirname(self._exe_path()), module), fast_load=True)
    pe.parse_data_directories([pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"]])
    slot = next(base + (s.address - pe.OPTIONAL_HEADER.ImageBase) for imp in pe.DIRECTORY_ENTRY_IMPORT
                for s in imp.imports if s.name == b"GetAsyncKeyState")
    buf = ctypes.c_uint64(); n = ctypes.c_size_t()
    k32.ReadProcessMemory(self.h, slot, ctypes.byref(buf), 8, ctypes.byref(n))
    self.mem = buf.value - KEY_STUB
    self.patched = ["(shared)"]
    return self


if __name__ == "__main__":
    # python vkeys.py <amdaemon pid> <vk hex> [ms]   press a key through an already attached keyboard
    import sys
    vkb = attached(int(sys.argv[1]))
    vkb.press(int(sys.argv[2], 16), int(sys.argv[3]) if len(sys.argv) > 3 else 150)
    print("pressed", sys.argv[2])
