using UnityEngine;

namespace Escape.Rooms
{
    public sealed class TsvIdAttribute : PropertyAttribute
    {
        public string AssetPath { get; }
        public string[] ExtraIds { get; }

        public TsvIdAttribute(string assetPath, params string[] extraIds)
        {
            AssetPath = assetPath;
            ExtraIds = extraIds;
        }
    }
}
