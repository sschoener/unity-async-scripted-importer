using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Experimental;
using UnityEngine;

namespace Editor
{
    public static class ImportTrigger
    {
        private static Queue<ArtifactKey> s_ImportsInFlight = new Queue<ArtifactKey>();
        private static int s_ProgressId = 0;

        static ImportTrigger()
        {
            // This just for the demo. In a real world scenario, you would probably track your progress differently.
            EditorApplication.update += CheckForImportsDone;
        }
        
        [MenuItem("Tool/Trigger Async Imports")]
        public static void TriggerAsyncImports()
        {
            // Lookup the guids for the assets of interest.
            // If any of these paths are invalid, then later calls to GetOnDemandArtifactProgress report a failure.
            var guids = new GUID[2];
            guids[0] = AssetDatabase.GUIDFromAssetPath("Assets/MyObjects/Object1.asset");
            guids[1] = AssetDatabase.GUIDFromAssetPath("Assets/MyObjects/Object2.asset");
            
            // The async imports run out-of-process. They do not see any data that we have in memory, they run on the
            // data that is on disk. Make sure to save whatever you need and asset import to see!
            AssetDatabase.SaveAssets();

            // Validate that the inputs are sensible. This is mandatory to avoid a quirk in the API. 
            for (int i = 0; i < guids.Length; i++)
            {
                if (guids[i].Empty())
                {
                    throw new Exception("One of the assets you asked for is invalid!");
                }
            }
            
            // Actually produce the imported artifacts.
            // We get an artifact id for every single import we request:
            //  * If the artifact still needs to be produced, the id is invalid.
            //  * If the artifact was already produced previously and we merely looked it up, it is valid.
            var importerType = typeof(MyScriptableObjectImporter);
            var artifactIds = AssetDatabaseExperimental.ProduceArtifactsAsync(
                guids,
                importerType
            );

            bool anyInFlight = false;
            for (int i = 0; i < artifactIds.Length; i++)
            {
                var key = new ArtifactKey(guids[i], importerType);
                if (artifactIds[i].isValid)
                    ReportImportDone(key, artifactIds[i], wasCached: true);
                else
                {
                    s_ImportsInFlight.Enqueue(key);
                    anyInFlight = true;
                }
            }

            // If your async imports are slow, it makes sense to show a progress bar for the background process.
            if (anyInFlight)
            {
                if (Progress.GetProgressById(s_ProgressId) == null)
                {
                    s_ProgressId = Progress.Start($"Importing assets)");
                    Progress.SetDescription(s_ProgressId, $"{s_ImportsInFlight.Count} remaining");
                }
            }
        }

        static string NiceArtifactKeyName(ArtifactKey key) =>
            $"{AssetDatabase.GUIDToAssetPath(key.guid)}/{nameof(MyScriptableObjectImporter)}";
        
        static void ReportImportDone(ArtifactKey artifactKey, ArtifactID artifactID, bool wasCached)
        {
            if (!AssetDatabaseExperimental.GetArtifactPaths(artifactID, out var paths))
            {
                // This codepath should never happen, unless the asset DB is broken.
                throw new Exception("Failed to get artifact paths for a valid artifact id. This should never happen.");
            }

            // In this case, we know that there is only a single path, and it's a json file. Let's read it and write
            // it to console.
            var json = File.ReadAllText(paths[0]);
            string status = wasCached ? "Import hit cache" : "Import finished";
            Debug.Log($"{status} for asset {NiceArtifactKeyName(artifactKey)}.\nJson:\n{json}\nArtifact paths:\n\t{string.Join("\n\t", paths)}");
        }
        
        static void CheckForImportsDone()
        {
            // We are going to check the imports one-by-one by calling
            //  AssetDatabaseExperimental.GetOnDemandArtifactProgress
            // We do this with a single one per frame here, but that is optional.
            // The common case where everything is cached has already been handled when we kick of imports.
            
            if (!s_ImportsInFlight.TryPeek(out var headKey))
            {
                if (s_ProgressId != 0)
                {
                    Progress.Finish(s_ProgressId);
                    s_ProgressId = 0;
                }
                return;
            }

            Progress.SetDescription(s_ProgressId, $"{s_ImportsInFlight.Count} remaining");
            var progress = AssetDatabaseExperimental.GetOnDemandArtifactProgress(headKey);
            switch (progress.state)
            {
                case OnDemandState.Unavailable:
                {
                    // This state is a bit unfortunate. It represents two different things:
                    //  * We haven't gotten to the thing you asked to import.
                    //  * The thing you asked for is invalid and just generally unavailable.
                    // The best way to handle this is to validate that what you ask to import makes sense (no invalid
                    // GUIDs) to rule out the second case.
                    
                    // There are some edge cases because this entire thing is async. For example, we could have requested
                    // an import for an asset and then in the meantime someone goes and deletes it. Great job, everyone.
                    // It's a rather rare problem, but we can detect it, so let's do that.
                    if (!headKey.isValid || headKey.importerType == null ||
                        string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(headKey.guid)))
                    {
                        Debug.LogError($"Import failed for asset {NiceArtifactKeyName(headKey)}: has it been deleted?");
                        s_ImportsInFlight.Dequeue();
                    }
                    break;
                }
                case OnDemandState.Processing:
                case OnDemandState.Downloading:
                {
                    // We don't do anything here. We just wait!
                    break;
                }
                case OnDemandState.Available:
                {
                    // Import finished!
                    s_ImportsInFlight.Dequeue();
                    var artifactID = AssetDatabaseExperimental.LookupArtifact(headKey);
                    if (!artifactID.isValid)
                    {
                        // This codepath should never happen, unless the asset DB is broken.
                        string niceArtifactKey = NiceArtifactKeyName(headKey);
                        throw new Exception($"Artifact ID for available artifact {niceArtifactKey} is invalid. This should never happen.");
                    }
                    ReportImportDone(headKey, artifactID, wasCached: false);
                    break;
                }
                case OnDemandState.Failed:
                {
                    // Import failed because your scripted importer failed.
                    Debug.LogError($"Import failed for asset {NiceArtifactKeyName(headKey)}");
                    s_ImportsInFlight.Dequeue();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}