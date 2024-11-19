using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Experimental;

// If you make any changes to the scripted importer, you need to bump the version number so existing cached imports are
// invalidated correctly. Similarly, you still need to be very careful to correctly declare all the dependencies of your
// code.
[ScriptedImporter(16, "ExtDontMatter", AllowCaching = true)]
public class MyScriptableObjectImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        // Your scriptable importer code goes here.
        // For debugging: note that this code doesn't necessarily run within the Unity main process.
        // This also has another side effect: This load here reads data from disk. If you modify your object in memory
        // but then don't save it to disk, the importer won't see the new version.
        MyScriptableObject obj = AssetDatabase.LoadAssetAtPath<MyScriptableObject>(ctx.assetPath);
        
        // We are loading the scriptable object, which is not a source asset but the primary artifact of an import.
        // For completeness, we should declare a dependency on that primary artifact. To be explicit, we construct the
        // key with a null importer type, which means that we are referring to the primary artifact.
        ctx.DependsOnArtifact(new ArtifactKey(AssetDatabase.GUIDFromAssetPath(ctx.assetPath), null));

        // simulate a slow import
        Thread.Sleep(1000);
        
        // Just write out some json representation of this object.
        // Note that we are touching the texture here, but we don't actually depend on the contents of the texture, so
        // we don't have to declare a dependency on it. That only works before we're working with GUIDs here, and the
        // GUID is already used in the scriptable object we are importing to reference the texture. So if the GUID changes,
        // our scriptable object would also have to change, hence no need to declare a dependency.
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj.MyTexture, out string guid, out long index);
        string outputJson = $"{{\"MyTexture\": \"{guid}:{index}\",\"MyString\": \"{obj.MyString}\"}}";
        
        var outputPath = ctx.GetOutputArtifactFilePath($"{obj.name}.json");
        File.WriteAllText(outputPath, outputJson);
    }
}
