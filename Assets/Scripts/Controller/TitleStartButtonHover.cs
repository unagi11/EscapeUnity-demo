using UnityEngine;
using UnityEngine.EventSystems;

namespace Escape.Controller
{
    /// <summary>시작 버튼의 누름과 해제를 타이틀 표정 제어기로 전달한다.</summary>
    public sealed class TitleStartButtonHover : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private TitleSceneController titleSceneController;

        /// <summary>시작 버튼을 누르는 순간 어두운 표정을 요청한다.</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            titleSceneController?.SetStartButtonPressed(true);
        }

        /// <summary>시작 버튼에서 손을 떼면 기본 표정 복귀를 요청한다.</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            titleSceneController?.SetStartButtonPressed(false);
        }

        /// <summary>버튼이 비활성화될 때 남아 있는 누름 상태를 해제한다.</summary>
        private void OnDisable()
        {
            titleSceneController?.SetStartButtonPressed(false);
        }
    }
}
