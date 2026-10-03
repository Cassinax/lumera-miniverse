using System.Collections.Generic;
using UnityEngine;
namespace Lumera.JumpForce
{
    [DefaultExecutionOrder(100)]
    public sealed class JumpForcePlatformVisibility : MonoBehaviour
    {
        public Camera view;
        public JumpForceCamera follow;
        [Range(0, 0.5f)] public float viewportMargin = 0.05f;
        sealed class Entry
        {
            public JumpForcePlatform platform;
            public Renderer[] renderers;
            public bool[] enabled;
            public bool retired;
        }
        readonly List<Entry> entries = new();
        void Start()
        {
            if (!follow) follow = GetComponent<JumpForceCamera>();
            if (!view) view = follow ? follow.Visao : GetComponentInChildren<Camera>();
            foreach (var platform in FindObjectsByType<JumpForcePlatform>())
            {
                if (platform.GetComponentInParent<JumpForceSpawnedElement>()) continue;
                var renderers = platform.GetComponentsInChildren<Renderer>(true);
                var enabled = new bool[renderers.Length];
                for (int i = 0; i < enabled.Length; i++) enabled[i] = renderers[i].enabled;
                entries.Add(new Entry { platform = platform, renderers = renderers, enabled = enabled });
            }
        }
        void LateUpdate() => RefreshNow();
        public void RefreshNow()
        {
            if (!view) return;
            foreach (var entry in entries)
            {
                var platform = entry.platform;
                if (!platform || (!platform.gameObject.activeSelf && !entry.retired)) continue;
                var box = platform.GetComponent<BoxCollider>();
                float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
                bool inFront = true;
                for (int i = 0; i < 8; i++)
                {
                    var offset = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                    var world = platform.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, offset));
                    var screen = view.WorldToViewportPoint(world);
                    inFront &= screen.z > view.nearClipPlane;
                    minY = Mathf.Min(minY, screen.y);
                    maxY = Mathf.Max(maxY, screen.y);
                }
                bool below = inFront && maxY < -viewportMargin;
                // Preserve the starting ground during the initial camera grace period.
                if (platform.startingGround && follow && !follow.LockedUpward) below = false;
                bool above = inFront && minY > 1 + viewportMargin;
                if (below)
                {
                    entry.retired = true;
                    platform.gameObject.SetActive(false);
                }
                else
                {
                    if (entry.retired) { platform.gameObject.SetActive(true); entry.retired = false; }
                    for (int i = 0; i < entry.renderers.Length; i++)
                        if (entry.renderers[i]) entry.renderers[i].enabled = entry.enabled[i] && !above;
                }
            }
        }
        public void RestoreAll()
        {
            foreach (var entry in entries)
            {
                if (!entry.platform) continue;
                if (entry.retired) entry.platform.gameObject.SetActive(true);
                entry.retired = false;
                for (int i = 0; i < entry.renderers.Length; i++)
                    if (entry.renderers[i]) entry.renderers[i].enabled = entry.enabled[i];
            }
        }
        void OnDisable() => RestoreAll();
    }
}
