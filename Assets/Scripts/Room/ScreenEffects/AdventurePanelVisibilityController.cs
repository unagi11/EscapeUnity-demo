using System;
using System.Collections.Generic;
using Escape.UI;
using UnityEngine;

namespace Escape.Rooms
{
    // 조사 패널의 중첩 숨김 요청과 자식 UI 표시 상태를 관리한다.
    internal sealed class AdventurePanelVisibilityController
    {
        private const string PanelObjectName = "AdventurePanelUI";

        private readonly Func<bool> canAccessScene;
        private readonly Func<string, Transform> findSceneChild;
        private readonly Dictionary<CanvasGroup, ChildCanvasState> childCanvasStates = new();
        private CanvasGroup panelCanvasGroup;
        private int hideRequestCount;

        private readonly struct ChildCanvasState
        {
            public readonly float Alpha;
            public readonly bool Interactable;
            public readonly bool BlocksRaycasts;

            // 자식 CanvasGroup의 원래 표시·입력 상태를 보관한다.
            public ChildCanvasState(CanvasGroup canvasGroup)
            {
                Alpha = canvasGroup.alpha;
                Interactable = canvasGroup.interactable;
                BlocksRaycasts = canvasGroup.blocksRaycasts;
            }
        }

        // 직렬화된 패널 참조와 씬 검색 수단을 연결한다.
        public AdventurePanelVisibilityController(
            CanvasGroup panelCanvasGroup,
            Func<bool> canAccessScene,
            Func<string, Transform> findSceneChild)
        {
            this.panelCanvasGroup = panelCanvasGroup;
            this.canAccessScene = canAccessScene;
            this.findSceneChild = findSceneChild;
        }

        // 숨김 요청을 누적하고 조사 패널의 자식 UI를 숨긴다.
        public void PushHidden()
        {
            if (!CanAccessScene())
            {
                return;
            }

            hideRequestCount++;
            SetVisible(false);
        }

        // 숨김 요청 하나를 해제하고 모든 요청이 끝나면 자식 UI를 복원한다.
        public void PopHidden()
        {
            if (!CanAccessScene())
            {
                return;
            }

            hideRequestCount = Mathf.Max(0, hideRequestCount - 1);
            if (hideRequestCount == 0)
            {
                SetVisible(true);
            }
        }

        // 인트로 중 조사 UI GameObject 자체의 활성 상태를 변경한다.
        public void SetActive(bool active)
        {
            ResolvePanelCanvasGroup();
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.gameObject.SetActive(active);
            }
        }

        // 소유 MonoBehaviour가 살아 있어 씬 참조에 접근할 수 있는지 확인한다.
        private bool CanAccessScene()
        {
            return canAccessScene == null || canAccessScene();
        }

        // 직렬화된 참조가 없으면 기존 씬 패널에서 CanvasGroup을 구한다.
        private void ResolvePanelCanvasGroup()
        {
            if (!CanAccessScene() || panelCanvasGroup != null)
            {
                return;
            }

            Transform panelTransform = findSceneChild?.Invoke(PanelObjectName);
            if (panelTransform == null)
            {
                return;
            }

            panelCanvasGroup = panelTransform.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = panelTransform.gameObject.AddComponent<CanvasGroup>();
            }
        }

        // 패널 루트는 유지하면서 입력과 직접 자식 UI 표시 상태를 변경한다.
        private void SetVisible(bool visible)
        {
            ResolvePanelCanvasGroup();
            if (panelCanvasGroup == null)
            {
                return;
            }

            panelCanvasGroup.alpha = 1f;
            SetInteractable(visible);
            SetChildrenVisible(visible);
        }

        // 조사 패널의 직접 자식 UI를 숨기고 원래 CanvasGroup 상태로 복원한다.
        private void SetChildrenVisible(bool visible)
        {
            Transform panelTransform = panelCanvasGroup != null
                ? panelCanvasGroup.transform
                : null;
            if (panelTransform == null)
            {
                return;
            }

            for (int i = 0; i < panelTransform.childCount; i++)
            {
                Transform child = panelTransform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                HealthBarSliderUI healthBar = child.GetComponentInChildren<HealthBarSliderUI>(true);
                if (healthBar != null)
                {
                    healthBar.SetAdventurePanelHidden(!visible);
                    continue;
                }

                CanvasGroup childCanvasGroup = child.GetComponent<CanvasGroup>();
                if (childCanvasGroup == null)
                {
                    childCanvasGroup = child.gameObject.AddComponent<CanvasGroup>();
                }

                if (!childCanvasStates.ContainsKey(childCanvasGroup))
                {
                    childCanvasStates[childCanvasGroup] = new ChildCanvasState(childCanvasGroup);
                }

                if (visible)
                {
                    ChildCanvasState state = childCanvasStates[childCanvasGroup];
                    childCanvasGroup.alpha = state.Alpha;
                    childCanvasGroup.interactable = state.Interactable;
                    childCanvasGroup.blocksRaycasts = state.BlocksRaycasts;
                    continue;
                }

                childCanvasGroup.alpha = 0f;
                childCanvasGroup.interactable = false;
                childCanvasGroup.blocksRaycasts = false;
            }
        }

        // 패널 루트의 입력과 레이캐스트 허용 여부를 함께 변경한다.
        private void SetInteractable(bool interactable)
        {
            if (panelCanvasGroup == null)
            {
                return;
            }

            panelCanvasGroup.interactable = interactable;
            panelCanvasGroup.blocksRaycasts = interactable;
        }
    }
}
