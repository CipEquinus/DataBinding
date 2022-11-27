using UnityEngine;

namespace de.JochenHeckl.Unity.DataBinding.Examples.GettingStarted
{
    public class DynamicMaterialColor : MonoBehaviour
    {
        public Material material;
        public Color Color
        {
            set
            {
                var meshRenderer = GetComponent<MeshRenderer>();
                meshRenderer.material.color = value;
            }
        }
    }
}
