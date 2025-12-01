using UnityEngine;

[CreateAssetMenu(menuName = "Items/Item Definition", fileName = "NewItem")]
public class ItemDefinition : ScriptableObject
{
    public string displayName = "Pickaxe";
    public GameObject prefab;
    public Vector3 localPosition;
    public Vector3 localEulerAngles;
    public Vector3 localScale = Vector3.one;
}