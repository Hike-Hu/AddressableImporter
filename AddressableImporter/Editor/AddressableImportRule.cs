﻿using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine.AddressableAssets;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityAddressableImporter.Helper;

public enum AddressableImportRuleMatchType
{
    /// <summary>
    /// Simple wildcard
    /// *, matches any number of characters
    /// ?, matches a single character
    /// </summary>
    [Tooltip("Simple wildcard.\n\"*\" matches any number of characters.\n\"?\" matches a single character.")]
    Wildcard = 0,

    /// <summary>
    /// Regex pattern
    /// </summary>
    [Tooltip("A regular expression pattern.")]
    Regex
}

public enum LabelWriteMode
{
    Add,
    Replace
}

public enum GroupTemplateApplicationMode
{
    ApplyOnGroupCreationOnly,
    AlwaysOverwriteGroupSettings
}

[System.Serializable]
public class AddressableImportRule
{
    /// <summary>
    /// Path pattern.
    /// </summary>
    [Tooltip("The assets in this path will be processed.")]
    public string path;

    public List<string> ExcludeExt = new List<string>();

    /// <summary>
    /// Method used to parse the Path.
    /// </summary>
    [Tooltip("The path parsing method.")]
    public AddressableImportRuleMatchType matchType;

    /// <summary>
    /// The group the asset will be added.
    /// </summary>
    [Tooltip("The group name in which the Addressable will be added. Leave blank for the default group.")]
    public string groupName;

    /// <summary>
    /// Defines if labels will be added or replaced.
    /// </summary>
    public LabelWriteMode LabelMode;

    /// <summary>
    /// Label reference list.
    /// </summary>
    [Tooltip("The list of labels to be added to the Addressable Asset")]
    public List<AssetLabelReference> labelRefs;

    /// <summary>
    /// Group template to use. Default Group settings will be used if empty.
    /// </summary>
    [Tooltip("Group template that will be applied to the Addressable Group. Leave none to use the Default Group's settings.")]
    public AddressableAssetGroupTemplate groupTemplate = null;

    /// <summary>
    /// Controls wether group template will be applied only on group creation, or also to already created groups.
    /// </summary>
    [Tooltip("Defines if the group template will only be applied to new groups, or will also overwrite existing groups settings.")]
    public GroupTemplateApplicationMode groupTemplateApplicationMode = GroupTemplateApplicationMode.ApplyOnGroupCreationOnly;

    /// <summary>
    /// Simplify address.
    /// </summary>
    [Tooltip("Simplify address to filename without extension.")]
    [Label("Address Simplified")]
    public bool simplified;

    /// <summary>
    /// Replacement string for the asset address. This is only useful with regex capture groups.
    /// </summary>
    [Tooltip("Replacement address string for regex matches.")]
    [ConditionalField("matchType", AddressableImportRuleMatchType.Regex, "simplified", false)]
    public string addressReplacement;

    public bool HasLabel
    {
        get
        {
            return labelRefs != null && labelRefs.Count > 0;
        }
    }

    /// <summary>
    /// Returns True if given assetPath matched with the rule.
    /// </summary>
    public bool Match(string assetPath)
    {
        path = path.Trim();
        if (string.IsNullOrEmpty(path))
            return false;
        if (matchType == AddressableImportRuleMatchType.Wildcard)
        {
            if (path.Contains("*") || path.Contains("?"))
            {
                var regex = "^" + Regex.Escape(path).Replace(@"\*", ".*").Replace(@"\?", ".");
                return Regex.IsMatch(assetPath, regex);
            }
            else
                return assetPath.StartsWith(path);
        }
        else if (matchType == AddressableImportRuleMatchType.Regex)
            return Regex.IsMatch(assetPath, path);
        return false;
    }

    /// <summary>
    /// Parse assetPath and replace all elements that match this.path regex
    /// with the groupName string.
    /// Returns null if this.path or groupName is empty.
    /// </summary>
    public string ParseGroupReplacement(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(groupName))
            return null;
        // Parse path elements.
        var replacement = AddressableImportRegex.ParsePath(assetPath, groupName);
        // Parse this.path regex.
        if (matchType == AddressableImportRuleMatchType.Regex) {
            string pathRegex = path;
            replacement = Regex.Replace(assetPath, pathRegex, replacement);
        }
        return replacement;
    }

    /// <summary>
    /// Parse assetPath and replace all elements that match this.path regex
    /// with the addressReplacement string.
    /// Returns assetPath if this.path or addressReplacement is empty.
    /// </summary>
    public string ParseAddressReplacement(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return assetPath;
        if (!simplified && string.IsNullOrWhiteSpace(addressReplacement))
            return assetPath;
        // Parse path elements.
        if (addressReplacement == null)
            addressReplacement = "";
        var replacement = AddressableImportRegex.ParsePath(assetPath, addressReplacement);
        // Parse this.path regex.
        // If Simplified is ticked, it's a pattern that matches any path, capturing the path, filename and extension.
        // If the match type is Wildcard, the pattern will match and capture the entire path string.
        string pathRegex =
            simplified
            ? @"(?<path>.*[/\\])+(?<filename>.+?)(?<extension>\.[^.]*$|$)"
            : (matchType == AddressableImportRuleMatchType.Wildcard
                ? @"(.*)"
                : path);
        replacement =
            simplified
            ? @"${filename}"
            : (matchType == AddressableImportRuleMatchType.Wildcard
                ? @"$1"
                : replacement);
        replacement = Regex.Replace(assetPath, pathRegex, replacement);
        return replacement;
    }

    public IEnumerable<string> labels
    {
        get
        {
            if (labelRefs == null)
                yield break;
            else
            {
                foreach (var labelRef in labelRefs)
                {
                    yield return labelRef.labelString;
                }
            }
        }
    }

    /// <summary>
    /// Helper class for regex replacement.
    /// </summary>
    static class AddressableImportRegex
    {
        const string pathregex = @"\$\{PATH\[\-{0,1}\d{1,3}\]\}"; // ie: ${PATH[0]} ${PATH[-1]}

        static public string[] GetPathArray(string path)
        {
            return path.Split('/');
        }

        static public string GetPathAtArray(string path, int idx)
        {
            return GetPathArray(path)[idx];
        }

        /// <summary>
        /// Parse assetPath and replace all matched path elements (i.e. `${PATH[0]}`)
        /// with a specified replacement string.
        /// </summary>
        static public string ParsePath(string assetPath, string replacement)
        {
            var _path = assetPath;
            int i = 0;
            var slashSplit = _path.Split('/');
            var len = slashSplit.Length - 1;
            var matches = Regex.Matches(replacement, pathregex);
            string[] parsedMatches = new string[matches.Count];
            foreach (var match in matches)
            {
                string v = match.ToString();
                var sidx = v.IndexOf('[') + 1;
                var eidx = v.IndexOf(']');
                int idx = int.Parse(v.Substring(sidx, eidx - sidx));
                while (idx > len)
                {
                    idx -= len;
                }
                while (idx < 0)
                {
                    idx += len;
                }
                //idx = Mathf.Clamp(idx, 0, slashSplit.Length - 1);
                parsedMatches[i++] = GetPathAtArray(_path, idx);
            }

            i = 0;
            var splitpath = Regex.Split(replacement, pathregex);
            string finalPath = string.Empty;
            foreach (var split in splitpath)
            {
                finalPath += splitpath[i];
                if (i < parsedMatches.Length)
                {
                    finalPath += parsedMatches[i];
                }
                i++;
            }
            return finalPath;
        }
    }
}
