using UnityEngine;

public class Whatever : MonoBehaviour
{
        
}

public class Test01
{
    public void Method(GameObject o)
    {
        o.GetComponent("Whate{caret}ver");
    }
}
