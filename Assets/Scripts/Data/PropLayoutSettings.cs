using System;
using UnityEngine;
using Yoegoe.Characters;

namespace Yoegoe.Data
{
    /// <summary>
    /// 기물 카탈로그·Prefab 참조. 맵 위 좌표는 Main 씬 Transform이 소스.
    /// FindByPropId·에디터 초기 배치(Yoegoe/Place Map & Props In Main Scene)에 사용.
    /// Assets/Resources/PropLayoutSettings.asset
    /// </summary>
    [CreateAssetMenu(fileName = "PropLayoutSettings", menuName = "Yoegoe/Prop Layout Settings")]
    public class PropLayoutSettings : ScriptableObject
    {
        [Serializable]
        public class Placement
        {
            public PropData data;
            [Tooltip("Assets/Prefabs/Props 기물 Prefab.")]
            public PropSlot prefab;
            [Tooltip("에디터 초기 배치용 맵 로컬 좌표 (mapScale=1 기준). 씬 배치 후엔 씬 Transform 우선.")]
            public Vector3 position;
        }

        public Placement[] placements;

        public static PropLayoutSettings Get()
        {
            var loaded = Resources.Load<PropLayoutSettings>("PropLayoutSettings");
            if (loaded != null) return loaded;
            return CreateInstance<PropLayoutSettings>();
        }

        public PropData FindByPropId(string propId)
        {
            if (string.IsNullOrEmpty(propId) || placements == null) return null;
            for (int i = 0; i < placements.Length; i++)
            {
                var p = placements[i];
                if (p?.data == null) continue;
                if (p.data.propId == propId || p.data.displayName == propId)
                    return p.data;
            }
            return null;
        }
    }
}
