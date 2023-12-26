#if UNITY_EDITOR
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using PluginSet.Core;
using UnityEngine;

namespace PluginSet.Patch
{
    public partial class FileManifest
    {
        private const int MaxWriteLength = 4194304;

        private const int CurrentVersion = 3;

        private struct SaveJson
        {
            public FileInfo[] Files;
        }

        private static void WriteInt(byte[] bytes, int value, ref int position)
        {
            for (int i = 0; i < 4; i++)
            {
                bytes[3 - i + position] = (byte)(value & 0xff);
                value >>= 8;
            }

            position += 4;
        }

        private static void WriteBytes(byte[] bytes, byte[] value, ref int position)
        {
            var len = value.Length;
            WriteInt(bytes, len, ref position);
            Array.Copy(value, 0, bytes, position, len);
            position += len;
        }

        private static void WriteString(byte[] bytes, string value, ref int position)
        {
            WriteInt(bytes, value.Length, ref position);
            WriteBytes(bytes, Encoding.UTF8.GetBytes(value), ref position);
        }

        /// <summary>
        /// 生成StreamingAssets文件 是在bundle构建完成后重新生成
        /// </summary>
        /// <param name="manifest">bundle文件信息</param>
        /// <param name="path">资源路径</param>
        /// <param name="targetName">生成的文件名 这里一般就是StreamingAssets</param>
        /// <param name="version">版本号</param>
        /// <param name="fileMap">用来记录文件组</param>
        /// <param name="subPatches"></param>
        /// <param name="tag">资源版本标识</param>
        public static void AppendFileInfo(AssetBundleManifest manifest, string path, string targetName, string version
            , ref Dictionary<string, FileInfo> fileMap, string[] subPatches = null, string tag = null)
        {
            var targetFileName = Path.Combine(path, targetName);
            byte[] targetContent = null;
            if (File.Exists(targetFileName))
            {
                targetContent = File.ReadAllBytes(targetFileName);
                if (!IsUnityManifest(targetContent))
                    return;
            }
            else
            {
                targetContent = new byte[0];
            }

            var fileList = new FileInfoList
            {
                List = new List<FileInfo>()
            };
            var list = fileList.List;
            var bundleFileList = manifest == null ? Array.Empty<string>() : manifest.GetAllAssetBundles();
            foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
            {
                if (IgnoreFile(file))
                    continue;

                var fileKey = Path.GetFileName(file);
                // 防止二次修改文件名
                if (!string.IsNullOrEmpty(fileKey) && fileMap.TryGetValue(fileKey, out var info))
                {
                    list.Add(info);
                    continue;
                }

                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName == null)
                    fileName = string.Empty;

                if (targetName.ToLower().Equals(fileName.ToLower()))
                    continue;

                var extension = Path.GetExtension(file);
                var content = File.ReadAllBytes(file);

                string bundleHash = string.Empty;
                var bundleName = Global.GetSubPath(path, file);
                var isBundleFile = bundleFileList.Contains(bundleName);
                // 对bundle加密
                if (isBundleFile)
                {
                    bundleHash = manifest.GetAssetBundleHash(bundleName).ToString();
                    content = AssetBundleEncryption.EncryptBytes(content, bundleHash);
                }

                var md5 = PluginUtil.GetMd5(content);

                string dstFileName;
                if (string.IsNullOrEmpty(fileName))
                    dstFileName = $"{md5}{extension}";
                else if (subPatches != null && subPatches.Contains(fileName))
                    dstFileName = fileName;
                else if (fileName.IndexOf(md5, StringComparison.Ordinal) < 0)
                    dstFileName = $"{fileName}_{md5}{extension}";
                else
                    dstFileName = fileName;

                File.Delete(file);
                File.WriteAllBytes(Path.Combine(path, dstFileName), content);

                info = new FileInfo
                {
                    Name = Global.GetSubPath(path, file),
                    FileName = dstFileName,
                    Size = content.Length,
                    Md5 = md5,
                    BundleHash = bundleHash
                };
                list.Add(info);
                fileMap.Add(dstFileName, info);
            }

            var saveJson = new SaveJson
            {
                Files = list.ToArray()
            };
            File.WriteAllText(Path.Combine(path, $"{targetName}_files.manifest"), JsonUtility.ToJson(saveJson, true));

            MemoryStream stream = new MemoryStream();
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(stream, fileList);

            var position = 0;
            var writeBuffer = new byte[MaxWriteLength];
            WriteInt(writeBuffer, CurrentVersion, ref position);
            WriteBytes(writeBuffer, targetContent, ref position);
            WriteBytes(writeBuffer, stream.ToArray(), ref position);
            WriteString(writeBuffer, version, ref position);
            WriteString(writeBuffer, PluginUtil.GetMd5(writeBuffer, 0, position), ref position);
            WriteString(writeBuffer, tag ?? string.Empty, ref position);
            
            if (subPatches == null)
            {
                WriteInt(writeBuffer, 0, ref position);
            }
            else
            {
                var count = subPatches.Length;
                WriteInt(writeBuffer, count, ref position);
                foreach (var sub in subPatches)
                {
                    WriteString(writeBuffer, sub, ref position);
                }
            }
            
            // remove by version 3
            // WriteInt(writeBuffer, fileList.List.Count, ref position);
            foreach (var fileInfo in fileList.List)
            {
                WriteFileInfo(writeBuffer, ref position, in fileInfo);
            }

            var buffer = new byte[position];
            Array.Copy(writeBuffer, buffer, position);
            //写入StreamingAssets文件
            File.WriteAllBytes(targetFileName, buffer);
        }
        
        private static void WriteFileInfo(byte[] buffer, ref int position, in FileInfo fileInfo)
        {
            WriteString(buffer, fileInfo.Name, ref position);
            WriteString(buffer, fileInfo.FileName, ref position);
            WriteInt(buffer, fileInfo.Size, ref position);
            WriteString(buffer, fileInfo.Md5, ref position);
            WriteString(buffer, fileInfo.BundleHash, ref position);
        }

        public static void AppendFileInfo(string filePath, params FileInfo[] fileInfos)
        {
            if (!File.Exists(filePath))
                throw new Exception($"file not exists {filePath}");
                
            var oldBuffer = File.ReadAllBytes(filePath);
            var position = oldBuffer.Length;
            var writeBuffer = new byte[MaxWriteLength];
            Array.Copy(oldBuffer, writeBuffer, position);
            foreach (var fileInfo in fileInfos)
            {
                WriteFileInfo(writeBuffer, ref position, in fileInfo);
            }
            
            var buffer = new byte[position];
            Array.Copy(writeBuffer, buffer, position);
            //写入StreamingAssets文件
            File.WriteAllBytes(filePath, buffer);
        }

        public static void AppendFiles(string path, string streamFileName, params string[] files)
        {
            var streamFilePath = Path.Combine(path, streamFileName);
            if (!File.Exists(streamFilePath))
                return;

            var list = new List<FileInfo>();
            foreach (var file in files)
            {
                var filePath = Path.Combine(path, file);
                var extension = Path.GetExtension(file);
                var content = File.ReadAllBytes(filePath);
                var md5 = PluginUtil.GetMd5(content);
                
                string dstFileName = $"{file}_{md5}{extension}";
                File.Move(filePath, Path.Combine(path, dstFileName));

                var info = new FileInfo
                {
                    Name = file,
                    FileName = dstFileName,
                    Size = content.Length,
                    Md5 = md5,
                    BundleHash = string.Empty
                };
                list.Add(info);
            }
            
            AppendFileInfo(streamFilePath, list.ToArray());
        }

        /// <summary>
        /// 忽略的文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static bool IgnoreFile(string fileName)
        {
            if (fileName.EndsWith(".manifest") || fileName.EndsWith(".meta"))
                return true;

            if (fileName.Contains(".DS_Store"))
                return true;

            return false;
        }

        /// <summary>
        /// 需要重新计算文件信息的文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static bool IsResetFile(string fileName) 
        {
            if (fileName.Contains("main") || fileName.Contains("files"))
                return true;

            return false;
        }
    }
}
#endif