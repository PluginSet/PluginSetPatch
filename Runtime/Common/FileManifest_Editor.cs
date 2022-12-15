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

        private const int CurrentVersion = 1;

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
            var bundleFileList = manifest.GetAllAssetBundles();
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

            var buffer = new byte[position];
            Array.Copy(writeBuffer, buffer, position);
            //写入StreamingAssets文件
            File.WriteAllBytes(targetFileName, buffer);
        }

        /// <summary>
        /// 生成StreamingAssets文件 在没有AssetBundleManifest时 就是在没有重新构建bundle时重新生成StreamingAssets,主要是为了更新版本号啥的
        /// </summary>
        /// <param name="path">资源路径</param>
        /// <param name="targetName">生成的文件名 这里一般就是StreamingAssets</param>
        /// <param name="version">版本号</param>
        /// <param name="fileMap">用来记录文件组</param>
        /// <param name="subPatches"></param>
        public static void AppendFileInfo(string path, string targetName, string version, ref Dictionary<string, FileInfo> fileMap, string[] subPatches = null)
        {
            var targetFileName = Path.Combine(path, targetName);
            byte[] targetContent = null;
            if (File.Exists(targetFileName))
            {
                targetContent = File.ReadAllBytes(targetFileName);
                //如果存在的StreamingAssets是unity生成的,那就有问题.
                if (IsUnityManifest(targetContent))
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
            FileManifest writeFileManifest = LoadFileManifest(string.Empty, targetFileName);
            foreach (var fileInfo in writeFileManifest.AllFileInfo)
            {
                string file = Path.Combine(path, fileInfo.FileName);
                if (IgnoreFile(file))
                    continue;

                var fileKey = Path.GetFileName(file);
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

                //部分文件需要重新计算大小和md5
                if (IsResetFile(fileInfo.FileName))
                {
                    var content = File.ReadAllBytes(file);
                    var md5 = PluginUtil.GetMd5(content);
                    info = new FileInfo
                    {
                        Name = fileInfo.Name,
                        FileName = fileInfo.FileName,
                        Size = content.Length,
                        Md5 = md5,
                        BundleHash = fileInfo.BundleHash
                    };
                    list.Add(info);
                }
                else
                {
                    info = new FileInfo
                    {
                        Name = fileInfo.Name,
                        FileName = fileInfo.FileName,
                        Size = fileInfo.Size,
                        Md5 = fileInfo.Md5,
                        BundleHash = fileInfo.BundleHash
                    };
                    list.Add(info);
                }
                fileMap.Add(fileInfo.FileName, info);
            }

            var saveJson = new SaveJson
            {
                Files = fileMap.Values.ToArray()
            };

            File.WriteAllText(Path.Combine(path, $"{targetName}_files.manifest"), JsonUtility.ToJson(saveJson, true));

            MemoryStream stream = new MemoryStream();
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(stream, fileList);

            var position = 0;
            var _fileVersion = ReadInt(targetContent, ref position);
            var _manifestBuffer = ReadBytes(targetContent, ref position);
            position = 0;

            //todo main he file 的md5还有问题 需要刷新

            var writeBuffer = new byte[MaxWriteLength];
            WriteInt(writeBuffer, _fileVersion, ref position);
            WriteBytes(writeBuffer, _manifestBuffer, ref position);
            WriteBytes(writeBuffer, stream.ToArray(), ref position);
            WriteString(writeBuffer, version, ref position);
            WriteString(writeBuffer, PluginUtil.GetMd5(writeBuffer, 0, position), ref position);
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

            var buffer = new byte[position];
            Array.Copy(writeBuffer, buffer, position);

            if (File.Exists(targetFileName))
                File.Delete(targetFileName);
            //写入StreamingAssets文件
            File.WriteAllBytes(targetFileName, buffer);
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