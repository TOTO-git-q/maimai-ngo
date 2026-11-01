using System;
using System.Collections.Generic;
using Manager;
using UnityEngine;
using UnityEngine.UI;

namespace PartnerLive2D.Unity
{
    // Fixed hardware-zone labels, never an input target. Only created on explicit debug opt-in.
    sealed class MusicSelectTouchDebug : IDisposable
    {
        readonly GameObject _root;
        readonly Dictionary<InputManager.TouchPanelArea, Text> _zones = new Dictionary<InputManager.TouchPanelArea, Text>();
        public MusicSelectTouchDebug(Transform main)
        {
            _root = new GameObject("PartnerLive2D_TouchZones", typeof(RectTransform));
            _root.layer = main.gameObject.layer; _root.transform.SetParent(main, false);
            _root.transform.SetSiblingIndex(main.Find("UI_Buttons").GetSiblingIndex());
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            foreach (InputManager.TouchPanelArea area in Enum.GetValues(typeof(InputManager.TouchPanelArea)))
            {
                string name = area.ToString(); if (name.Length != 2) continue;
                int i = name[1] - '1'; char ring = name[0];
                float radius = ring == 'A' || ring == 'D' ? 490 : ring == 'B' ? 295 : ring == 'E' ? 205 : 0;
                float angle = (45 * i + (ring == 'A' || ring == 'B' ? 22.5f : 0)) * Mathf.Deg2Rad;
                var go = new GameObject(name, typeof(RectTransform), typeof(Text));
                go.layer = main.gameObject.layer; go.transform.SetParent(_root.transform, false);
                var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(56, 32);
                rt.localPosition = new Vector3(Mathf.Sin(angle) * radius + (ring == 'C' ? (i == 0 ? -30 : 30) : 0), Mathf.Cos(angle) * radius, 0);
                var text = go.GetComponent<Text>(); text.font = font; text.fontSize = 24; text.alignment = TextAnchor.MiddleCenter;
                text.text = name; text.raycastTarget = false; _zones.Add(area, text);
            }
        }
        public void Tick(int player)
        {
            foreach (var zone in _zones) zone.Value.color = InputManager.GetTouchPanelAreaPush(player, zone.Key) ? Color.yellow : new Color(1, 1, 1, .45f);
        }
        public void Dispose() { if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); } }
    }
}
