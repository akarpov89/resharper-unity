using UnityEngine;

public class Whatever : MonoBehaviour
{
        
}

public class Test03 : MonoBehaviour
{
    public void Method()
    {
        var t = GetComponent("Whate{caret}ver");
    }
}