using UnityEngine;

[CreateAssetMenu(fileName = "MyScriptableObject", menuName = "My ScriptableObject")]
public class MyScriptableObject : ScriptableObject
{
    public Texture MyTexture;
    public string MyString;
}
