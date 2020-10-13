using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public class AddressableImporter : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        //return;
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var importSettings = AddressableImportSettings.Instance;

        if (importSettings == null || settings == null)
        {
            Debug.LogFormat("settings or importSettings is null");
            return;
        }
        if (importSettings.rules == null || importSettings.rules.Count == 0) return;

        var entriesAdded = new List<AddressableAssetEntry>();
        foreach (string assetPath in importedAssets)
        {
            foreach (var rule in importSettings.rules)
            {
                if (rule.Match(assetPath))
                {
                    var ext = Path.GetExtension(assetPath);
                    if (rule.ExcludeExt.Contains(ext)) continue;

                    var entry = CreateOrUpdateAddressableAssetEntry(settings, importSettings, rule, assetPath);
                    if (entry == null) continue;

                    entriesAdded.Add(entry);
                    if (rule.HasLabel)
                        Debug.LogFormat("[AddressableImporter] Entry created/updated for {0} with address {1} and labels {2}", assetPath, entry.address, string.Join(", ", entry.labels));
                    else
                        Debug.LogFormat("[AddressableImporter] Entry created/updated for {0} with address {1}", assetPath, entry.address);
                }
            }
        }

        if (movedAssets != null)
        {
            foreach (var assetPath in movedAssets)
            {
                foreach (var rule in importSettings.rules)
                {
                    if (rule.Match(assetPath))
                    {
                        var ext = Path.GetExtension(assetPath);
                        if (rule.ExcludeExt.Contains(ext)) continue;

                        var entry = CreateOrUpdateAddressableAssetEntry(settings, importSettings, rule, assetPath);
                        if (entry == null) continue;

                        entriesAdded.Add(entry);
                        if (rule.HasLabel)
                            Debug.LogFormat("[AddressableImporter] Entry created/updated for {0} with address {1} and labels {2}", assetPath, entry.address, string.Join(", ", entry.labels));
                        else
                            Debug.LogFormat("[AddressableImporter] Entry created/updated for {0} with address {1}", assetPath, entry.address);
                    }

                }
            }
        }

        if (deletedAssets != null)
        {
            importSettings.removeEmtpyGroups = true;
            for (int i = 0; i < deletedAssets.Length; i++)
            {
                var assetPath = deletedAssets[i];
                DelectedAddressableAssetEntry(assetPath);

            }
        }

        if (entriesAdded.Count > 0)
        {
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entriesAdded, true);
            AssetDatabase.SaveAssets();
        }

        foreach (var addressableAssetGroup in settings.groups)
        {
            if (addressableAssetGroup == null)
            {
                settings.RemoveAssetEntry(addressableAssetGroup.Guid);
                settings.RemoveGroup(addressableAssetGroup);
            }
        }

        if (importSettings.removeEmtpyGroups)
        {
            RemoveEmptyGroup();
        }

    }

    public static void RemoveEmptyGroup()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        settings.groups.RemoveAll(group =>
            {
                if (!group) return true;

                FindMissingAssetEntry(group);

                var  bol = group.entries.Count == 0 && !group.IsDefaultGroup();

                if (group.ReadOnly) return bol;

                if (!bol && group.Schemas.Count < 2)
                {
                    AddSchemas(group);
                }
                return bol;
            }
        );
    }

    static AddressableAssetGroup CreateAssetGroup(AddressableAssetSettings settings, string groupName)
    {
        return settings.CreateGroup(groupName, false, false, false, null, typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema));
    }

    static AddressableAssetEntry CreateOrUpdateAddressableAssetEntry(
        AddressableAssetSettings settings,
        AddressableImportSettings importSettings,
        AddressableImportRule rule,
        string assetPath)
    {
        // Set group
        AddressableAssetGroup group;
        var groupName = rule.ParseGroupReplacement(assetPath);
        bool newGroup = false;
        if (!TryGetGroup(settings, groupName, out group))
        {
            if (importSettings.allowGroupCreation)
            {
                //TODO Specify on editor which type to create.
                group = CreateAssetGroup(settings, groupName);
                newGroup = true;
            }
            else
            {
                Debug.LogErrorFormat("[AddressableImporter] Failed to find group {0} when importing {1}. Please check if the group exists, then reimport the asset.", rule.groupName, assetPath);
                return null;
            }
        }

        // Set group settings from template if necessary
        if (rule.groupTemplate != null && (newGroup || rule.groupTemplateApplicationMode == GroupTemplateApplicationMode.AlwaysOverwriteGroupSettings))
        {
            rule.groupTemplate.ApplyToAddressableAssetGroup(group);
        }

        var guid = AssetDatabase.AssetPathToGUID(assetPath);

        var entry = group.GetAssetEntry(guid);
        if (entry == null)
        {
            entry = settings.CreateOrMoveEntry(guid, group);
        }

        if (entry != null)
        {
            if (rule.LabelMode == LabelWriteMode.Replace)
                entry.labels.Clear();
            foreach (var label in rule.labels)
            {
                entry.labels.Add(label);
            }

            // Apply address replacement if address is empty or path.
            if (string.IsNullOrEmpty(entry.address) ||
                entry.address.StartsWith("Assets/") ||
                rule.simplified ||
                !string.IsNullOrWhiteSpace(rule.addressReplacement)
                )
            {
                var newPath = rule.ParseAddressReplacement(assetPath);
                if (entry.address.Equals(newPath))
                {
                    return null;
                }

                entry.address = newPath;
            }
        }
        return entry;
    }

    static void DelectedAddressableAssetEntry(string assetPath)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (assetPath.Contains("StoryAssets"))
        {
            var assetname = assetPath.Split(new char[] { '\\', '/' }).LastOrDefault();
            var groupName = $"StoryAssets_{assetname}";
            var g = settings.groups.Find(group => group.Name == groupName);
            if (g)
            {
                settings.RemoveAssetEntry(g.Guid);
                settings.RemoveGroup(g);
            }
        }
    }
    /// <summary>
    /// Attempts to get the group using the provided <paramref name="groupName"/>.
    /// </summary>
    /// <param name="settings">Reference to the <see cref="AddressableAssetSettings"/></param>
    /// <param name="groupName">The name of the group for the search.</param>
    /// <param name="group">The <see cref="AddressableAssetGroup"/> if found. Set to <see cref="null"/> if not found.</param>
    /// <returns>True if a group is found.</returns>
    static bool TryGetGroup(AddressableAssetSettings settings, string groupName, out AddressableAssetGroup group)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            group = settings.DefaultGroup;
            return true;
        }
        return ((group = settings.groups.Find(g => string.Equals(g.Name, groupName.Trim()))) == null) ? false : true;
    }

    public static void FindMissingAssetEntry(AddressableAssetGroup group)
    {
        if (group.entries.Count == 0) return;

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var list = group.entries.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].AssetPath == "")
            {
                settings.RemoveAssetEntry(list[i].guid);
            }
        }
    }

    public static void AddSchemas(AddressableAssetGroup group)
    {
        for (int i = 0; i < group.Schemas.Count; i++)
        {
            var sc = group.Schemas[i];
            group.RemoveSchema(sc.GetType());
        }

        group.AddSchema<ContentUpdateGroupSchema>();
        group.AddSchema<BundledAssetGroupSchema>();

    }

    /// <summary>
    /// Allows assets within the selected folder to be checked agains the Addressable Importer rules.
    /// </summary>
    public class FolderImporter
    {
        [MenuItem("Assets/一键生成所有Groups资源（Story除外）")]
        public static void CreateAddressableGroup()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Setting丢失,请重新生成");
                return;
            }

            CheckGroupAssets();


            var assetPath = "Assets/Prefabs";
            var filesToAdd = Directory.GetFiles(assetPath, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in filesToAdd)
            {
                var path = file.Replace(".meta", "");

                CreateGroupAssect(path);
                Debug.Log($"Success:{path}");
            }
            CreateGroupEnd();
        }

        [MenuItem("Assets/一键生成Story资源")]
        public static void CreateStoryAddressableGroup()
        {

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Group丢失,请点击重新生成");
                return;
            }

            CheckGroupAssets();

            var assetPath = "Assets/StoryAssets";
            HashSet<string> filesToImport = new HashSet<string>();
            // Other assets may appear as Object, so a Directory Check filters directories from folders.

            if (Directory.Exists(assetPath))
            {
                var filesToAdd = Directory.GetFiles(assetPath, "*", SearchOption.AllDirectories);
                foreach (var file in filesToAdd)
                {
                    // If Directory.GetFiles accepted Regular Expressions, we could filter the metas before iterating.
                    if (!file.EndsWith(".meta"))
                    {
                        filesToImport.Add(file.Replace('\\', '/'));
                    }
                }
            }

            if (filesToImport.Count > 0)
            {
                Debug.Log($"AddressablesImporter: Found {filesToImport.Count} assets...");

                var entriesAdded = new List<AddressableAssetEntry>();
                foreach (string assetPath2 in filesToImport.ToArray())
                {
                    //ClearGroup(assetPath2);
                    AddressableAssetGroup group;
                    AddressableAssetEntry entry;
                    var t = assetPath2.Split('/');
                    var groupName = $"StoryAssets_{t.LastOrDefault()}";
                    bool newGroup = false;
                    if (!TryGetGroup(settings, groupName, out group))
                    {
                        group = CreateAssetGroup(settings, groupName);
                        newGroup = true;
                    }

                    var guid = AssetDatabase.AssetPathToGUID(assetPath2);

                    entry = group.GetAssetEntry(guid);

                    if (entry == null)
                    {
                        entry = settings.CreateOrMoveEntry(guid, group);
                    }
                    if (entry != null)
                    {
                        entry.labels.Clear();
                        entry.SetLabel("Story", true);

                    }

                    if (entry == null) continue;

                    entriesAdded.Add(entry);
                }

                if (entriesAdded.Count > 0)
                {
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entriesAdded, true);
                    AssetDatabase.SaveAssets();
                }

            }
            else
            {
                Debug.Log($"AddressablesImporter: No files to reimport");
            }

            CreateGroupEnd();
        }

        [MenuItem("Assets/一键生成所有资源")]
        public static void CreateAllAddressableGroup()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("Setting丢失,请重新生成");
                return;
            }

            CheckGroupAssets();

            var assetPathPrefabs = "Assets/Prefabs";
            var filesToAddPrefabs = Directory.GetFiles(assetPathPrefabs, "*", SearchOption.TopDirectoryOnly);
            foreach (var filePrefabs in filesToAddPrefabs)
            {
                var path = filePrefabs.Replace(".meta", "");

                CreateGroupAssect(path);
                Debug.Log($"Success:{path}");
            }

            var assetPath = "Assets/StoryAssets";
            HashSet<string> filesToImport = new HashSet<string>();

            if (Directory.Exists(assetPath))
            {
                var filesToAdd = Directory.GetFiles(assetPath, "*", SearchOption.AllDirectories);
                foreach (var file in filesToAdd)
                {
                    if (!file.EndsWith(".meta"))
                    {
                        filesToImport.Add(file.Replace('\\', '/'));
                    }
                }
            }

            if (filesToImport.Count > 0)
            {
                Debug.Log($"AddressablesImporter: Found {filesToImport.Count} assets...");

                var entriesAdded = new List<AddressableAssetEntry>();
                foreach (string assetPath2 in filesToImport.ToArray())
                {
                    //ClearGroup(assetPath2);
                    AddressableAssetGroup group;
                    AddressableAssetEntry entry;
                    var t = assetPath2.Split('/');
                    var groupName = $"StoryAssets_{t.LastOrDefault()}";
                    bool newGroup = false;
                    if (!TryGetGroup(settings, groupName, out group))
                    {
                        group = CreateAssetGroup(settings, groupName);
                        newGroup = true;
                    }

                    var guid = AssetDatabase.AssetPathToGUID(assetPath2);

                    entry = group.GetAssetEntry(guid);

                    if (entry == null)
                    {
                        entry = settings.CreateOrMoveEntry(guid, group);
                    }
                    if (entry != null)
                    {
                        entry.labels.Clear();
                        entry.SetLabel("Story", true);

                    }

                    if (entry == null) continue;

                    entriesAdded.Add(entry);
                }

                if (entriesAdded.Count > 0)
                {
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entriesAdded, true);
                    AssetDatabase.SaveAssets();
                }

            }

            CreateGroupEnd();
        }

        private static void CheckGroupAssets()
        {
            RemoveEmptyGroup();
            CheckBuiltInData();
        }

        private static void CreateGroupAssect(string path)
        {
            HashSet<string> filesToImport = new HashSet<string>();
            // Other assets may appear as Object, so a Directory Check filters directories from folders.
            if (Directory.Exists(path))
            {
                var filesToAdd = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (var file in filesToAdd)
                {
                    // If Directory.GetFiles accepted Regular Expressions, we could filter the metas before iterating.
                    if (!file.EndsWith(".meta") && !file.EndsWith(".DS_Store"))
                    {
                        filesToImport.Add(file.Replace('\\', '/'));
                    }
                }
            }

            if (filesToImport.Count > 0)
            {
                //ClearGroup(path);
                Debug.Log($"AddressablesImporter: Found {filesToImport.Count} assets...");
                OnPostprocessAllAssets(filesToImport.ToArray(), null, null, null);
            }
            else
            {
                Debug.Log($"AddressablesImporter: No files to reimport");
            }
        }

        private static void CreateGroupEnd()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            DelectUnUseFiles();

            settings.ActivePlayModeDataBuilder.BuildData<AddressableAssetBuildResult>(
                new AddressablesDataBuilderInput(settings));
            EditorUtility.SetDirty(settings);
            
            Debug.Log("生成完毕");
            
            //EditorUtility.DisplayDialog("", "生成完毕", "确定");
        }

        private static void DelectUnUseFiles()
        {
            RemoveEmptyGroup();

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var groupsPath = $"Assets/AddressableAssetsData/AssetGroups";

            //删Group
            var files = Directory.GetFiles(groupsPath);
            if (files.Length != settings.groups.Count)
            {
                var delectgroupfiles = files.ToList().FindAll(file =>
                {

                    var group = settings.groups.Find(g => { return file.Contains(g.Name); });
                    return !group;
                });
                for (int i = 0; i < delectgroupfiles.Count; i++)
                {
                    AssetDatabase.DeleteAsset(delectgroupfiles[i]);
                    //File.Delete(delectgroupfiles[i]);
                }
            }


            //删Schemas
            var tempfiles = Directory.GetFiles($"{groupsPath}/Schemas", "*.asset");
            if (tempfiles.Length > settings.groups.Count * 2)
            {
                var delectschemasfiles = tempfiles.ToList().FindAll(file =>
                {
                    var group = settings.groups.Find(g => { return file.Contains(g.Name); });
                    return !group;
                });
                for (int i = 0; i < delectschemasfiles.Count; i++)
                {
                    AssetDatabase.DeleteAsset(delectschemasfiles[i]);
                }

            }
            AssetDatabase.Refresh();
        }

        private const string PlayerDataGroupName = "Built In Data";
        private const string ResourcesName = "Resources";
        private const string ResourcesPath = "*/Resources/";
        private const string EditorSceneListName = "EditorSceneList";
        private static void CheckBuiltInData()
        {
            var setting = AddressableAssetSettingsDefaultObject.Settings;

            if (setting.groups.Count == 0 || !setting.FindGroup(PlayerDataGroupName))
            {
                CreateBuiltInData(setting);
            }

            
            //    AssetDatabase.SaveAssets();

        }
       
        private static void CreateBuiltInData(AddressableAssetSettings aa)
        {
            var playerData = aa.CreateGroup(PlayerDataGroupName, false, true, false, null , typeof(ContentUpdateGroupSchema), typeof(BundledAssetGroupSchema), typeof(PlayerDataGroupSchema));
            var resourceEntry = aa.CreateOrMoveEntry(ResourcesName, playerData);
            resourceEntry.IsInResources = true;
            resourceEntry.SetLabel("default", true);
           var sceneEntry= aa.CreateOrMoveEntry(EditorSceneListName, playerData);
           sceneEntry.SetLabel("default", true);


        }
        
    }
}
