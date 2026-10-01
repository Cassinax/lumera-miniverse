using System;
using UnityEngine;

namespace Lumera.JumpForce
{
    // Runtime data generated together with Carlos_Paleta.png. Authoring remains in Carlos_Paletas.
    public sealed class JumpForcePaletteCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Option
        {
            public string name;
            public Color swatch = Color.white;
        }
        public Option[] options = Array.Empty<Option>();
        public int defaultIndex = 1;
    }
}