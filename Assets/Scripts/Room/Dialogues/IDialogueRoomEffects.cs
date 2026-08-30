using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Escape.Rooms
{
    // DialoguePlayer가 방 상태를 변경할 때 필요한 최소 기능만 정의한다.
    internal interface IDialogueRoomEffects
    {
        Transform CurrentRoom { get; }

        // 대사 effect에서 현재 방의 손전등 점등 연출을 실행한다.
        UniTask PlayFlashlightTurnOnEffectAsync(CancellationToken cancellationToken);

        // 대사 effect가 요청한 방으로 연출음 없이 이동한다.
        void MoveToRoomFromDialogue(RoomType destination);

        // 대사 effect가 지정한 씬 오브젝트의 활성 상태를 변경한다.
        bool SetSceneObjectActive(string objectName, bool active);
    }
}
