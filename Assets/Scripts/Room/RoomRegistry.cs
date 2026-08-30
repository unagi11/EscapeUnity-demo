using System.Collections.Generic;
using UnityEngine;

namespace Escape.Rooms
{
    // 씬의 Room 컴포넌트를 중복 없이 등록하고 탐색·이동 기능에 제공한다.
    internal sealed class RoomRegistry
    {
        private readonly Dictionary<RoomType, Room> roomsById = new();
        private readonly List<Room> rooms = new();

        public int Count => rooms.Count;

        // 직렬화 참조와 현재 씬 계층에서 Room 컴포넌트를 다시 수집한다.
        public void Resolve(IEnumerable<Room> configuredRooms, Transform searchRoot, GameObject sceneOwner)
        {
            roomsById.Clear();
            rooms.Clear();

            Register(configuredRooms);
            if (searchRoot != null)
            {
                Register(searchRoot.GetComponentsInChildren<Room>(true));
            }

            if (sceneOwner != null)
            {
                var scene = sceneOwner.scene;
                if (scene.IsValid())
                {
                    foreach (GameObject rootObject in scene.GetRootGameObjects())
                    {
                        Register(rootObject.GetComponentsInChildren<Room>(true));
                    }
                }
            }

            if (rooms.Count == 0)
            {
                Debug.LogWarning("RoomController could not find any Room components.", sceneOwner);
            }
        }

        // 지정 ID에 등록된 Room의 Transform을 반환한다.
        public Transform GetTransform(RoomType roomId)
        {
            return roomsById.TryGetValue(roomId, out Room room) && room != null
                ? room.transform
                : null;
        }

        // 동일 Transform을 한 번만 반환하며 등록된 Room 루트를 순회한다.
        public IEnumerable<Transform> EnumerateRoots()
        {
            var seen = new HashSet<Transform>();
            for (int i = 0; i < rooms.Count; i++)
            {
                Room room = rooms[i];
                if (room != null && seen.Add(room.transform))
                {
                    yield return room.transform;
                }
            }
        }

        // 후보 Room 목록을 기존 등록 상태와 합친다.
        private void Register(IEnumerable<Room> candidates)
        {
            if (candidates == null)
            {
                return;
            }

            foreach (Room room in candidates)
            {
                Register(room);
            }
        }

        // 유효한 Room 하나를 목록과 ID 조회표에 등록한다.
        private void Register(Room room)
        {
            if (room == null || room.RoomId == RoomType.None || rooms.Contains(room))
            {
                return;
            }

            rooms.Add(room);
            if (roomsById.ContainsKey(room.RoomId))
            {
                Debug.LogWarning($"Duplicate room id ignored: {room.RoomId} ({room.name})", room);
                return;
            }

            roomsById.Add(room.RoomId, room);
        }
    }
}
