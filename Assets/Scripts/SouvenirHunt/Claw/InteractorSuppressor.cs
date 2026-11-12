using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace SouvenirHunt
{
    /// <summary>
    /// 인형뽑기 씬에서 XRI 레이(원거리 캐스팅·빔·레티클)와 컨트롤러 모델을 숨긴다 — 3차부터 빔을 전혀 쓰지 않으므로
    /// ResetGame(부팅)부터 세션 내내 억제, OnDestroy에서만 복원. (Beam Pro를 눕혀 들면 빔이 옆으로 뻗고 모델이 시야에 떠 몰입을 깬다)
    /// 리그는 SDK 프리팹이라 이름 대신 컴포넌트 타입으로 찾고, 상태를 캐시해 원복한다.
    /// </summary>
    public class InteractorSuppressor : MonoBehaviour
    {
        class Entry
        {
            public NearFarInteractor nf;
            public bool farWas;
            public readonly List<Behaviour> behaviours = new();
            public readonly List<Renderer> renderers = new();
        }

        readonly List<Entry> entries = new();
        public bool Suppressed { get; private set; }

        public void SetSuppressed(bool on)
        {
            if (entries.Count == 0) Scan();
            if (Suppressed == on) return;
            Suppressed = on;
            foreach (var e in entries)
            {
                try
                {
                    if (e.nf != null) e.nf.enableFarCasting = on ? false : e.farWas;
                    foreach (var b in e.behaviours) if (b != null) b.enabled = !on;
                    foreach (var r in e.renderers) if (r != null) r.enabled = !on;
                }
                catch (System.Exception ex) { Debug.LogWarning("[InteractorSuppressor] " + ex.Message); }
            }
        }

        void Scan()
        {
            entries.Clear();
            foreach (var nf in FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var e = new Entry { nf = nf, farWas = nf.enableFarCasting };
                e.behaviours.AddRange(nf.GetComponentsInChildren<CurveVisualController>(true));
                e.behaviours.AddRange(nf.GetComponents<XRInteractorReticleVisual>());
                e.renderers.AddRange(nf.GetComponentsInChildren<LineRenderer>(true));
                // 컨트롤러 모델: 인터랙터의 부모("Right Controller") 아래 메시 — 인터랙터 자신의 자식은 제외
                var parent = nf.transform.parent;
                if (parent != null)
                    foreach (var mr in parent.GetComponentsInChildren<MeshRenderer>(true))
                        if (!mr.transform.IsChildOf(nf.transform)) e.renderers.Add(mr);
                entries.Add(e);
            }
            if (entries.Count == 0) Debug.LogWarning("[InteractorSuppressor] NearFarInteractor 없음 — 레이 억제 생략(앱 정상)");
        }

        void OnDestroy()
        {
            if (Suppressed) SetSuppressed(false);
        }
    }
}
