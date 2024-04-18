using System;
using UnityEngine;

namespace Htex
{
    [Serializable]
    public class TextureInfo : ScriptableObject
    {
        public int numFaces;
        public Vector2Int quadSize;
        public Vector2Int numQuads;
    }
}
