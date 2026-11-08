"""Run actual release scripts against disposable touched-file games, never D:.

python ngo_test_release.py <release folder> <work folder>
Fixtures and evidence are retained; an existing matrix run is not overwritten.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path, PureWindowsPath
import shutil
import subprocess
import time


def sha(path):
    with Path(path).open('rb') as fi:
        return hashlib.file_digest(fi, 'sha256').hexdigest()


def local(root, rel):
    return root.joinpath(*PureWindowsPath(rel).parts)


def save(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def inventory(root):
    return {p.relative_to(root).as_posix(): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}


def tree_hash(data):
    return hashlib.sha256(''.join(f'{r}\0{h}\n' for r, h in sorted(data.items())).encode()).hexdigest()


def psquote(s):
    return "'" + str(s).replace("'", "''") + "'"


class Matrix:
    def __init__(self, release, work):
        self.release, self.work = release.resolve(), work.resolve()
        self.root = self.work / 'fixtures'
        if self.root.exists() or (self.work/'results.json').exists():
            raise FileExistsError('Use a fresh work folder; prior fixture evidence is retained')
        if self.root.drive.lower() == 'd:' or self.release in self.root.parents or self.root in self.release.parents:
            raise ValueError('Tests require a separate, writable fixture root outside D:')
        self.root.mkdir(parents=True)
        self.manifest = json.loads((release/'manifest.json').read_text('utf-8-sig'))
        self.stock = {f['path']: f['sha256'] for f in self.manifest['stock_files']}
        self.settings = self.manifest['settings_path']
        self.results = []
        self.worklog = self.work.parent.parent/'worklog.md'
        self.started = time.time()

    def step(self, name, data):
        result = {'name': name, 'status': 'PASS', **data}
        self.results.append(result)
        save(self.work/'results.json', {'results': self.results, 'elapsed_seconds': time.time()-self.started})
        with self.worklog.open('a', encoding='utf-8') as f:
            f.write(f'\n- Test step verified: {name}; evidence validation/{self.work.name}/results.json; next: remaining matrix/final verification.\n')
        print('PASS', name, flush=True)

    def distribution(self, name, source):
        dest = self.root/name/'release'
        # Only immutable distribution data is hardlinked; installed game files
        # are always independent copies, so Copy-Item cannot alter source data.
        def copy(src, dst):
            rel = Path(src).relative_to(source)
            if rel.parts[0] in ('files', 'original'):
                os.link(src, dst)
            else:
                shutil.copy2(src, dst)
            return dst
        shutil.copytree(source, dest, copy_function=copy,
                        ignore=lambda d, names: [n for n in names if n.startswith('backup_')])
        return dest

    def game(self, name):
        root = self.root/name/'Package'
        for rel in self.stock:
            target = local(root, rel)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(local(self.release/'original', rel), target)
        for rel in ['segatools.ini', 'DEVICE/aime.txt', 'AquaMai.toml', 'config_fixture.json',
                    'appdata/save_fixture.bin', 'LocalAssets/PartnerLive2D/player-note.txt', 'Mods/OtherFixture.dll']:
            target = root/rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(('PRIVATE SENTINEL ' + rel + '\n').encode())
        return root

    def invoke(self, dist, game, action, label, expected=0, mock=None):
        assert game.is_relative_to(self.root) and game.drive.lower() != 'd:'
        command = '[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false); '
        if mock:
            command += mock + '; '
        command += '& ' + psquote(dist/(action+'.ps1')) + ' -Package ' + psquote(game)
        started = time.time()
        result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', command],
                                capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
        output = (result.stdout + result.stderr).decode('utf-8', 'replace')
        log = self.work/(label+'.log')
        log.write_text(output + f'\nExit code: {result.returncode}\nSeconds: {time.time()-started:.3f}\n', encoding='utf-8')
        if (result.returncode == 0) != (expected == 0):
            raise AssertionError(f'{label}: code {result.returncode}; see {log}\n{output}')
        return output

    def snapshot(self, game, label):
        data = inventory(game)
        save(self.work/(label+'.hashes.json'), {'tree_sha256': tree_hash(data), 'files': data})
        return data

    def check_snapshot(self, dist, before, label):
        backups = sorted(dist.glob('backup_before_install_*'), key=lambda p:p.stat().st_mtime_ns)
        backup = backups[-1]
        snap = json.loads((backup/'snapshot.json').read_text('utf-8-sig'))
        for rel, existed in snap['existed'].items():
            if existed:
                assert sha(local(backup, rel)) == before[rel.replace('\\','/')], (label,rel)
        return backup

    def run(self):
        expected_files = {f['path'].replace('\\','/'): f['sha256'] for f in self.manifest['files']}
        # First representative path is the entire real fresh install.
        game = self.game('fresh')
        stock_state = self.snapshot(game,'stock')
        dist = self.distribution('fresh',self.release)
        self.invoke(dist,game,'install','fresh-install')
        fresh = self.snapshot(game,'fresh-installed')
        assert all(fresh[r] == h for r,h in expected_files.items())
        self.check_snapshot(dist,stock_state,'fresh')
        self.invoke(dist,game,'uninstall','fresh-uninstall')
        assert self.snapshot(game,'fresh-uninstalled') == stock_state
        self.step('stock + 1103; uninstall returns stock',{'installed_tree_sha256':tree_hash(fresh),'stock_tree_sha256':tree_hash(stock_state)})

        for version in ('20261101','20261031'):
            label = 'upgrade-'+version
            game = self.game(label)
            old = self.distribution(label+'-old',self.release.parent/('maimai_NGO_skin_'+version))
            self.invoke(old,game,'install',label+'-old-install')
            before = self.snapshot(game,label+'-before')
            # Delete nothing in old release; make its backup unreachable to the
            # new script by moving this disposable old distribution away.
            hidden = old.parent/'old-package-out-of-reach'
            old.rename(hidden)
            new = self.distribution(label,self.release)
            self.invoke(new,game,'install',label+'-install')
            after = self.snapshot(game,label+'-installed')
            assert after == fresh, label
            backup = self.check_snapshot(new,before,label)
            retired = sorted(set(r for v in self.manifest['previous_versions'] for r in v['added_files']) -
                             set(f['path'] for f in self.manifest['files']))
            for rel in retired:
                assert not local(game,rel).exists()
                assert sha(local(backup,rel)) == before[rel.replace('\\','/')]
            self.invoke(new,game,'uninstall',label+'-uninstall')
            assert self.snapshot(game,label+'-uninstalled') == stock_state
            self.step(label+' equals fresh 1103; uninstall returns stock',{'installed_tree_sha256':tree_hash(after),'stock_tree_sha256':tree_hash(stock_state),'removed_and_backed_up':retired})

        game = self.game('reinstall')
        dist = self.distribution('reinstall',self.release)
        self.invoke(dist,game,'install','reinstall-first')
        first = self.snapshot(game,'reinstall-first')
        self.invoke(dist,game,'install','reinstall-second')
        second = self.snapshot(game,'reinstall-second')
        assert first == second == fresh
        self.check_snapshot(dist,first,'reinstall')
        assert len(list(dist.glob('backup_before_install_*'))) == 2
        self.invoke(dist,game,'uninstall','reinstall-uninstall')
        assert self.snapshot(game,'reinstall-uninstalled') == stock_state
        self.step('1103 + 1103 stable; uninstall returns stock',{'first_tree_sha256':tree_hash(first),'second_tree_sha256':tree_hash(second),'stock_tree_sha256':tree_hash(stock_state)})

        game = self.game('edited-settings')
        old = self.distribution('edited-settings-old',self.release.parent/'maimai_NGO_skin_20261101')
        self.invoke(old,game,'install','edited-settings-old-install')
        settings = local(game,self.settings)
        edited = settings.read_text('utf-8-sig').replace('selectView = true','selectView = false').replace('freedomEntry = false','freedomEntry = true')
        edited += '\ncustomPlayerKey = kept\ncharaLevel = 42\n'
        settings.write_text(edited,encoding='utf-8')
        before = self.snapshot(game,'edited-settings-before')
        dist = self.distribution('edited-settings',self.release)
        self.invoke(dist,game,'install','edited-settings-upgrade')
        backup = self.check_snapshot(dist,before,'edited-settings')
        text = settings.read_text('utf-8-sig')
        for line in ('selectView = false','freedomEntry = true','lateFrameLog = false','customPlayerKey = kept','charaLevel = 42'):
            assert line in text, line
        after = self.snapshot(game,'edited-settings-after')
        self.invoke(dist,game,'install','edited-settings-reinstall')
        assert self.snapshot(game,'edited-settings-reinstalled') == after
        self.invoke(dist,game,'uninstall','edited-settings-uninstall')
        assert self.snapshot(game,'edited-settings-uninstalled') == stock_state
        self.step('player edits and new defaults survive upgrade/reinstall; uninstall returns stock',
                  {'settings_before_sha256':before[self.settings.replace('\\','/')],
                   'settings_after_sha256':after[self.settings.replace('\\','/')],
                   'old_settings_backup_sha256':sha(local(backup,self.settings))})
        self.negative_checks()
        self.step('complete full matrix',{'elapsed_seconds':time.time()-self.started})

    def negative_checks(self):
        # Guards exercise actual scripts with a controlled process-list double;
        # no executable named Sinmai is launched.
        game = self.game('guards')
        dist = self.distribution('guards',self.release)
        before = inventory(game)
        for action in ('install','uninstall'):
            for label,path in [('running',str(game/'Sinmai.exe')),('unreadable',None)]:
                val = '$null' if path is None else psquote(path)
                mock = 'function Get-Process { param($Name,$ErrorAction); [pscustomobject]@{ Path = '+val+' } }'
                self.invoke(dist,game,action,'guard-'+action+'-'+label,expected=1,mock=mock)
                assert inventory(game) == before
        assert not list(dist.glob('backup_*'))
        # Corrupt an independent payload copy, never its hardlinked original.
        rel = next(f['path'] for f in self.manifest['files'] if f['path'].lower().endswith('.dll'))
        bad = local(dist/'files',rel)
        bad.unlink(); bad.write_bytes(b'corrupt payload test')
        self.invoke(dist,game,'install','guard-corrupt-payload',expected=1)
        assert inventory(game) == before and not list(dist.glob('backup_*'))
        bad.unlink(); os.link(local(self.release/'files',rel),bad)
        rel = next(iter(self.stock))
        bad = local(dist/'original',rel)
        bad.unlink(); bad.write_bytes(b'corrupt stock test')
        self.invoke(dist,game,'uninstall','guard-corrupt-original',expected=1)
        assert inventory(game) == before and not list(dist.glob('backup_*'))
        self.step('running-game/unreadable-process and integrity guards refuse before writes',{'stock_tree_sha256':tree_hash(before),'real_game_launched':False})


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('release',type=Path); parser.add_argument('work',type=Path)
    a=parser.parse_args()
    Matrix(a.release,a.work).run()


if __name__ == '__main__':
    main()
