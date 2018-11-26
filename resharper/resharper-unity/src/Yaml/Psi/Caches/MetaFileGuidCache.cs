using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.DataFlow;
using JetBrains.ReSharper.Plugins.Yaml.Psi;
using JetBrains.ReSharper.Plugins.Yaml.Psi.Tree;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.Util;
using JetBrains.Util.DataStructures;

namespace JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches
{
    [PsiComponent]
    public class MetaFileGuidCache : SimpleICache<MetaFileCacheItem>
    {
        // We expect to only get one asset with a given guid, but copy/pasting .meta files could break that.
        // CompactOneToListMap is optimised for the typical use case of only one item per key
        private readonly CompactOneToListMap<string, FileSystemPath> myAssetGuidToAssetFilePaths =
            new CompactOneToListMap<string, FileSystemPath>();

        // Note that Map is a map of *meta file* to asset guid, NOT asset file!
        private readonly Dictionary<FileSystemPath, string> myAssetFilePathToGuid =
            new Dictionary<FileSystemPath, string>();

        public MetaFileGuidCache(Lifetime lifetime, IPersistentIndexManager persistentIndexManager)
            : base(lifetime, persistentIndexManager, MetaFileCacheItem.Marshaller)
        {
#if DEBUG
            ClearOnLoad = true;
#endif
        }

        // This will usually return a single value, but there's always a chance for copy/paste
        // Also note that this returns the file path of the asset associated with the GUID, not the asset's .meta file!
        public IList<FileSystemPath> GetAssetFilePathsFromGuid(string guid)
        {
            return myAssetGuidToAssetFilePaths[guid];
        }

        public IList<string> GetAssetNames(string guid)
        {
            return myAssetGuidToAssetFilePaths[guid].Select(p => p.NameWithoutExtension).ToList();
        }

        [CanBeNull]
        public string GetAssetGuid(IPsiSourceFile sourceFile)
        {
            return myAssetFilePathToGuid.TryGetValue(sourceFile.GetLocation(), out var guid) ? guid : null;
        }

        protected override bool IsApplicable(IPsiSourceFile sf)
        {
            return sf.IsLanguageSupported<YamlLanguage>() &&
                   sf.Name.EndsWith(".cs.meta", StringComparison.InvariantCultureIgnoreCase);
        }

        public override object Build(IPsiSourceFile sourceFile, bool isStartup)
        {
            if (!IsApplicable(sourceFile))
                return null;

            if (!(sourceFile.GetDominantPsiFile<YamlLanguage>() is IYamlFile yamlFile))
                return null;

            var document = yamlFile.Documents.FirstOrDefault();
            if (document?.BlockNode is IBlockMappingNode blockMappingNode)
            {
                foreach (var entry in blockMappingNode.Entries)
                {
                    if (entry.Key?.CompareBufferText("guid") == true && entry.Value is IPlainScalarNode valueScalarNode)
                    {
                        var guid = valueScalarNode.Text?.GetText();
                        if (guid != null)
                            return new MetaFileCacheItem(guid);
                    }
                }
            }

            return null;
        }

        public override void Merge(IPsiSourceFile sourceFile, object builtPart)
        {
            if (builtPart == null)
                CleanLocalCache(sourceFile);

            base.Merge(sourceFile, builtPart);
            PopulateLocalCache(sourceFile, builtPart as MetaFileCacheItem);
        }

        public override void MergeLoaded(object data)
        {
            base.MergeLoaded(data);

            foreach (var (psiSourceFile, cacheItem) in Map)
                PopulateLocalCache(psiSourceFile, cacheItem);
        }

        public override void Drop(IPsiSourceFile sourceFile)
        {
            CleanLocalCache(sourceFile);
            base.Drop(sourceFile);
        }

        private void PopulateLocalCache(IPsiSourceFile metaFile, [CanBeNull] MetaFileCacheItem data)
        {
            if (data == null) return;

            var metaFileLocation = metaFile.GetLocation();
            if (!metaFileLocation.IsEmpty)
            {
                var assetLocation = GetAssetLocationFromMetaFile(metaFileLocation);
                myAssetGuidToAssetFilePaths.AddValue(data.Guid, assetLocation);
                myAssetFilePathToGuid.Add(assetLocation, data.Guid);
            }
        }

        private void CleanLocalCache(IPsiSourceFile sourceFile)
        {
            if (Map.TryGetValue(sourceFile, out var cacheItem))
            {
                var assetLocation = GetAssetLocationFromMetaFile(sourceFile.GetLocation());
                myAssetGuidToAssetFilePaths.RemoveValue(cacheItem.Guid, assetLocation);
                myAssetFilePathToGuid.Remove(assetLocation);
            }
        }

        private static FileSystemPath GetAssetLocationFromMetaFile(FileSystemPath metaFileLocation)
        {
            return metaFileLocation.ChangeExtension(string.Empty);
        }
    }
}