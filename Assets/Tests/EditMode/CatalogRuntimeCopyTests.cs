using NUnit.Framework;
using UnityEngine;
using Yoegoe.Data;

namespace Yoegoe.Tests.EditMode
{
    /// <summary>시트 값은 런타임 사본에만 — 에셋 원본은 그대로 (에디터 플레이 후 .asset 변경 방지).</summary>
    public class CatalogRuntimeCopyTests
    {
        [Test]
        public void PropRuntimeCopy_AppliesSheet_LeavesSourceUntouched()
        {
            var src = ScriptableObject.CreateInstance<PropData>();
            src.propId = "옹달샘";
            src.displayName = "옛 이름";
            src.resourceType = PropResourceType.Merit;

            var copy = PropCatalog.RuntimeCopy(src);

            Assert.AreNotSame(src, copy);
            Assert.AreEqual(PropResourceType.Water, copy.resourceType);
            Assert.AreEqual(15, copy.baseCapacity); // v1.2 보관 15
            Assert.AreEqual(PropResourceType.Merit, src.resourceType);
            Assert.AreEqual("옛 이름", src.displayName);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(src);
        }

        [Test]
        public void CharacterRuntimeCopy_AppliesSheet_LeavesSourceUntouched()
        {
            var src = ScriptableObject.CreateInstance<CharacterData>();
            src.id = CharacterId.SamjokO;
            src.displayName = "옛 이름";

            var copy = CharacterCatalog.RuntimeCopy(src);

            Assert.AreEqual("삼족오", copy.displayName);
            Assert.AreEqual("옛 이름", src.displayName);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(src);
        }
    }
}
