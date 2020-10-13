﻿using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityAddressableImporter.Helper;


[CreateAssetMenu(fileName = "AddressableImportSettings", menuName = "Addressables/Import Settings", order = 50)]
public class AddressableImportSettings : ScriptableObject
{
    public const string kDefaultConfigObjectName = "addressableimportsettings";
    public const string kDefaultPath = "Assets/AddressableImporter/AddressableImportSettings.asset";

    [Tooltip("Creates a group if the specified group doesn't exist.")]
    public bool allowGroupCreation = false;

    [Tooltip("Removes groups without addressables except the default group.")]
    public bool removeEmtpyGroups = false;

    [Tooltip("Rules for managing imported assets.")]
    public List<AddressableImportRule> rules;

    [ButtonMethod]
    private void Save()
    {
        AssetDatabase.SaveAssets();
    }

    [ButtonMethod]
    private void Documentation()
    {
        Application.OpenURL("https://github.com/favoyang/unity-addressable-importer/blob/master/Documentation~/AddressableImporter.md");
    }

    public static AddressableImportSettings Instance
    {
        get
        {
            AddressableImportSettings so;
            // Try to locate settings via EditorBuildSettings.
            if (EditorBuildSettings.TryGetConfigObject(kDefaultConfigObjectName, out so))
                return so;
            // Try to locate settings via path.
            so = AssetDatabase.LoadAssetAtPath<AddressableImportSettings>(kDefaultPath);
            if (so != null)
                EditorBuildSettings.AddConfigObject(kDefaultConfigObjectName, so, true);
            return so;
        }
    }
}