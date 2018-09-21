using UnityEngine;

namespace DefaultNamespace
{
    public class ConstantValueTest
    {
        public const string test = "test";
        private static readonly int Property = Shader.PropertyToID(test + "Foo")
            
        public void Method(Material material)
        {
            material.SetFloat("te{caret}stFoo", 10.0f);
        }
    }
}