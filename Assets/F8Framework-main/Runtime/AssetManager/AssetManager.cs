using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace F8Framework.Core
{
    //异步加载完成的回调
    public delegate void OnAssetObject<T>(T obj)
        where T : Object;
    
    public class AssetManager : ModuleSingleton<AssetManager>, IModule
    {
        private AssetBundleManager _assetBundleManager;

        private ResourcesManager _resourcesManager;
        
        //强制更改资产加载模式为远程（微信小游戏使用）
        public static bool ForceRemoteAssetBundle = false;
        
        //资产信息
        public struct AssetInfo
        {
            //目标资产类型
            public readonly AssetTypeEnum AssetType;
            
            //直接资产请求路径相对路径，Assets开头的
            public readonly string[] AssetPath;
            
            //直接资产捆绑请求路径（仅适用于资产捆绑类型），完全路径
            public readonly string AssetBundlePath;
            
            //AB名
            public readonly string AbName;
            
            public AssetInfo(
                AssetTypeEnum assetType,
                string[] assetPath,
                string assetBundlePathWithoutAb,
                string abName)
            {
                AssetType = assetType;
                AssetPath = assetPath;
                AssetBundlePath = assetBundlePathWithoutAb + abName;
                AbName = abName;
            }

        }
             //资产访问标志
            [System.Flags]
            public enum AssetAccessMode
            {
                NONE = 0b1,
                UNKNOWN = 0b10,
                RESOURCE = 0b100,
                ASSET_BUNDLE = 0b1000,
                REMOTE_ASSET_BUNDLE = 0b10000
            }

            //资产类型
            public enum AssetTypeEnum
            {
                NONE,
                RESOURCE,
                ASSET_BUNDLE
            }
            
            // 是否采用编辑器模式
            private bool _isEditorMode = false;
            public bool IsEditorMode
            {
                get
                {
#if UNITY_EDITOR
                    return _isEditorMode;
#else
                    return false;
#endif
                }
                set
                {
                    _isEditorMode = value;
                }
            }
            
            
            //如果信息合法，则该值为真
            public bool IsLegal(ref AssetInfo assetInfo)
            {
                if (assetInfo.AssetType == AssetTypeEnum.NONE)
                    return false;

                if (assetInfo.AssetType == AssetTypeEnum.RESOURCE &&
                    assetInfo.AssetPath == null)
                    return false;

                if (assetInfo.AssetType == AssetTypeEnum.ASSET_BUNDLE &&
                    (assetInfo.AssetPath == null || assetInfo.AssetBundlePath == null))
                    return false;

                return true;
            }
            
            /// <summary>
            /// 根据提供的资产路径和访问选项推断资产类型。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="accessMode">访问模式。</param>
            /// <returns>资产信息。</returns>
            public AssetInfo GetAssetInfo(string assetName,
                AssetAccessMode accessMode = AssetAccessMode.UNKNOWN)
            {
                if (ForceRemoteAssetBundle)
                {
                    accessMode = AssetAccessMode.REMOTE_ASSET_BUNDLE;
                }
                
                if (accessMode.HasFlag(AssetAccessMode.RESOURCE))
                {
                    return GetAssetInfoFromResource(assetName, showTip: true);
                }
                else if (accessMode.HasFlag(AssetAccessMode.ASSET_BUNDLE))
                {
                    return GetAssetInfoFromAssetBundle(assetName, showTip: true);
                }
                else if (accessMode.HasFlag(AssetAccessMode.UNKNOWN))
                {
                    AssetInfo r = GetAssetInfoFromAssetBundle(assetName);
                    if (!IsLegal(ref r))
                    {
                        r = GetAssetInfoFromResource(assetName);
                    }

                    if (IsLegal(ref r))
                    {
                        return r;
                    }
                    else
                    {
                        LogF8.LogError("AssetBundle和Resource都找不到指定资源可用的索引：" + assetName);
                        return new AssetInfo();
                    }
                }
                else if (accessMode.HasFlag(AssetAccessMode.REMOTE_ASSET_BUNDLE))
                {
                    AssetInfo r = GetAssetInfoFromAssetBundle(assetName, true);

                    if (IsLegal(ref r))
                    {
                        return r;
                    }
                    else
                    {
                        LogF8.LogError("AssetBundle找不到指定远程资源可用的索引：" + assetName);
                        return new AssetInfo();
                    }
                }
                return new AssetInfo();
            }

            /// <summary>
            /// 通过资产捆绑加载程序和对象名称获取资产对象。
            /// </summary>
            /// <typeparam name="T">资产对象的目标对象类型。</typeparam>
            /// <param name="assetName">资产对象的路径。</param>
            /// <param name="mode">访问模式。</param>
            /// <returns>找到的资产对象。</returns>
            public T GetAssetObject<T>(
                string assetName,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
                where T : Object
            {
                AssetInfo info = GetAssetInfo(assetName, mode);

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    T o = ResourcesManager.Instance.GetResouceObject<T>(info.AssetPath[0]);
                    return o;
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(info.AssetPath[0]);
                    }
#endif
                    T o = AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                    if (o != null)
                    {
                        return o;
                    }
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    ab.Expand();
                    o = AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                    if (o != null)
                    {
                        return o;
                    }
                    LogF8.LogError("获取不到资产AssetObject");
                }

                return null;
            }

            /// <summary>
            /// 同步加载资源对象。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="assetType">目标资产类型。</param>
            /// <param name="mode">访问模式。</param>
            /// <returns>加载的资产对象。</returns>
            public Object GetAssetObject(
                string assetName,
                System.Type assetType,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                
                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    Object o = ResourcesManager.Instance.GetResouceObject(info.AssetPath[0], assetType);
                    return o;
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath(info.AssetPath[0], assetType);
                    }
#endif
                    Object o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType);
                    if (o != null)
                    {
                        return o;
                    }
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    ab.Expand();
                    o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType);
                    if (o != null)
                    {
                        return o;
                    }
                    LogF8.LogError("获取不到资产AssetObject");
                }

                return null;
            }

            /// <summary>
            /// 同步加载资源对象。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="mode">访问模式。</param>
            /// <returns>加载的资产对象。</returns>
            public Object GetAssetObject(
                string assetName,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                
                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    Object o = ResourcesManager.Instance.GetResouceObject(info.AssetPath[0]);
                    return o;
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(info.AssetPath[0]);
                    }
#endif
                    Object o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName);
                    if (o != null)
                    {
                        return o;
                    }
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    ab.Expand();
                    o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName);
                    if (o != null)
                    {
                        return o;
                    }
                    LogF8.LogError("获取不到资产AssetObject");
                }

                return null;
            }
            
            /// <summary>
            /// 同步加载资源对象。
            /// </summary>
            /// <typeparam name="T">目标资产类型。</typeparam>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="mode">访问模式。</param>
            /// <returns>加载的资产对象。</returns>
            public T Load<T>(
                string assetName,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
                where T : Object
            {
                
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                    return null;
                
                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    T o = ResourcesManager.Instance.GetResouceObject<T>(info.AssetPath[0]);
                    if (o != null)
                    {
                        return o;
                    }
                      
                    return ResourcesManager.Instance.Load<T>(info.AssetPath[0]);
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(info.AssetPath[0]);
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null ||
                        ab.AssetBundleContent == null)
                    {
                        AssetBundleManager.Instance.Load(assetName, ref info);
                        ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    }
                
                    T o = AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                    if (o != null)
                    {
                        return o;
                    }
                    
                    ab.Expand();
                    return AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                }

                return null;
            }
            
            /// <summary>
            /// 同步加载资源文件夹。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="mode">访问模式。</param>
            public void LoadDir(
                string assetName,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                    return;
                
                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    LogF8.LogAsset("Resources不支持加载文件夹功能");
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return;
                    }
#endif
                    foreach (var assetPath in info.AssetPath)
                    {
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }
                        string subAssetName = Path.ChangeExtension(assetPath, null).Replace(URLSetting.AssetBundlesPath, "");
                        string abName = subAssetName.ToLower();
                        AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName) + abName);
                        if (ab == null || ab.AssetBundleContent == null)
                        {
                            AssetInfo assetInfo = new AssetInfo(info.AssetType, new []{assetPath}, AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName), abName);
                            AssetBundleManager.Instance.Load(Path.GetFileNameWithoutExtension(assetPath), 
                                ref assetInfo);
                        }
                    }
                }
            }
            
            /// <summary>
            /// 同步加载资源对象。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="assetType">目标资产类型。</param>
            /// <param name="mode">访问模式。</param>
            /// <returns>加载的资产对象。</returns>
            public Object Load(
                string assetName,
                System.Type assetType,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                    return null;

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    Object o = ResourcesManager.Instance.GetResouceObject(info.AssetPath[0], assetType);
                    if (o != null)
                    {
                        return o;
                    }

                    return ResourcesManager.Instance.Load(info.AssetPath[0], assetType);
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath(info.AssetPath[0], assetType);
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null ||
                        ab.AssetBundleContent == null)
                    {
                        AssetBundleManager.Instance.Load(assetName, ref info);
                        ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    }
            
                    Object o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType);
                    if (o != null)
                    {
                        return o;
                    }
                
                    ab.Expand();
                    return AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType);
                }

                return null;
            }

            /// <summary>
            /// 同步加载资源对象。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="mode">访问模式。</param>
            /// <returns>加载的资产对象。</returns>
            public Object Load(
                string assetName,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                    return null;

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    Object o = ResourcesManager.Instance.GetResouceObject(info.AssetPath[0]);
                    if (o != null)
                    {
                        return o;
                    }

                    return ResourcesManager.Instance.Load(info.AssetPath[0]);
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        return UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(info.AssetPath[0]);
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null ||
                        ab.AssetBundleContent == null)
                    {
                        AssetBundleManager.Instance.Load(assetName, ref info);
                        ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    }
            
                    Object o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName);
                    if (o != null)
                    {
                        return o;
                    }
                    ab.Expand();
                    return AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName);
                }

                return null;
            }
            
            /// <summary>
            /// 异步加载资产对象。
            /// </summary>
            /// <typeparam name="T">目标资产类型。</typeparam>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="callback">异步加载完成时的回调函数。</param>
            /// <param name="mode">访问模式。</param>
            public void LoadAsync<T>(
                string assetName,
                OnAssetObject<T> callback = null,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
                where T : Object
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                {
                    End();
                    return;
                }

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    T o = ResourcesManager.Instance.GetResouceObject<T>(info.AssetPath[0]);
                    if (o != null)
                    {
                        End(o);
                        return;
                    }
                    ResourcesManager.Instance.LoadAsync<T>(info.AssetPath[0], callback);
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        T o = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(info.AssetPath[0]);
                        End(o);
                        return;
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null || ab.AssetBundleContent == null || ab.GetDependentNamesLoadFinished() < ab.AddDependentNames())
                    {
                        AssetBundleManager.Instance.LoadAsync(assetName, info, (b) => {
                            End(AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName));
                        });
                        return;
                    }
                    else
                    {
                        T o = AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                        if (o != null)
                        {
                            End(o);
                            return;
                        }
                        
                        ab.Expand();
                        End(AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName));
                    }
                }

                void End(T o = null)
                {
                    callback?.Invoke(o);
                }
            }
            
            /// <summary>
            /// 协程加载资产对象。
            /// </summary>
            /// <typeparam name="T">目标资产类型。</typeparam>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="mode">访问模式。</param>
            public IEnumerator LoadAsyncCoroutine<T>(string assetName, AssetAccessMode mode = AssetAccessMode.UNKNOWN) where T : Object
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                {
                    yield break;
                }

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    T o = ResourcesManager.Instance.GetResouceObject<T>(info.AssetPath[0]);
                    if (o != null)
                    {
                        yield return o;
                    }
                    else
                    {
                        yield return ResourcesManager.Instance.LoadAsyncCoroutine<T>(info.AssetPath[0]);
                    }
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        T o = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(info.AssetPath[0]);
                        yield return o;
                        yield break;
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null || ab.AssetBundleContent == null || ab.GetDependentNamesLoadFinished() < ab.AddDependentNames())
                    {
                        yield return AssetBundleManager.Instance.LoadAsyncCoroutine(assetName, info);
                        yield return AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                    }
                    else
                    {
                        T o = AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                        if (o != null)
                        {
                            yield return o;
                        }
                        else
                        {
                            ab.Expand();
                            yield return AssetBundleManager.Instance.GetAssetObject<T>(info.AssetBundlePath, info.AbName);
                        }
                    }
                }
            }
            
            /// <summary>
            /// 异步加载资产文件夹。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="callback">异步加载完成时的回调函数。</param>
            /// <param name="mode">访问模式。</param>
            public void LoadDirAsync(
                string assetName,
                Action callback = null,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                {
                    End();
                    return;
                }

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    LogF8.LogAsset("Resources不支持加载文件夹功能");
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        LogF8.LogAsset("编辑器模式下无需加载文件夹");
                        End();
                        return;
                    }
#endif
                    int assetCount = 0;
                    foreach (var assetPath in info.AssetPath)
                    {
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }
                        string subAssetName = Path.ChangeExtension(assetPath, null).Replace(URLSetting.AssetBundlesPath, "");
                        string abName = subAssetName.ToLower();
                        AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName) + abName);
                        if (ab == null || ab.AssetBundleContent == null || ab.GetDependentNamesLoadFinished() < ab.AddDependentNames())
                        {
                            AssetBundleManager.Instance.LoadAsync(Path.GetFileNameWithoutExtension(assetPath), 
                                new AssetInfo(info.AssetType, new []{assetPath}, AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName), abName), (b) =>
                                {
                                    if (++assetCount >= info.AssetPath.Length)
                                    {
                                        End();
                                    }
                                });
                        }
                        else
                        {
                            Object o = AssetBundleManager.Instance.GetAssetObject(AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName), abName);
                            if (o != null)
                            {
                                if (++assetCount >= info.AssetPath.Length)
                                {
                                    End();
                                }
                                continue;
                            }
                            
                            ab.Expand();
                            if (++assetCount >= info.AssetPath.Length)
                            {
                                End();
                            }
                        }
                    }
                }

                void End()
                {
                    callback?.Invoke();
                }
            }
            
            /// <summary>
            /// 协程加载资产文件夹。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="mode">访问模式。</param>
            public IEnumerable LoadDirAsyncCoroutine(string assetName, AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                {
                    yield break;
                }

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    LogF8.LogAsset("Resources不支持加载文件夹功能");
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        LogF8.LogAsset("编辑器模式下无需加载文件夹");
                        yield break;
                    }
#endif
                    int assetCount = 0;
                    foreach (var assetPath in info.AssetPath)
                    {
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }
                        string subAssetName = Path.ChangeExtension(assetPath, null).Replace(URLSetting.AssetBundlesPath, "");
                        string abName = subAssetName.ToLower();
                        AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName) + abName);
                        if (ab == null || ab.AssetBundleContent == null || ab.GetDependentNamesLoadFinished() < ab.AddDependentNames())
                        {
                            yield return AssetBundleManager.Instance.LoadAsyncCoroutine(Path.GetFileNameWithoutExtension(assetPath),
                                new AssetInfo(info.AssetType, new[] { assetPath }, AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName), abName));
                            if (++assetCount >= info.AssetPath.Length)
                            {
                                yield break;
                            }
                        }
                        else
                        {
                            Object o = AssetBundleManager.Instance.GetAssetObject(AssetBundleManager.GetAssetBundlePathWithoutAb(subAssetName), abName);
                            if (o != null)
                            {
                                if (++assetCount >= info.AssetPath.Length)
                                {
                                    yield break;
                                }
                                continue;
                            }
                            
                            ab.Expand();
                            if (++assetCount >= info.AssetPath.Length)
                            {
                                yield break;
                            }
                        }
                    }
                }
            }

            /// <summary>
            /// 异步加载资产对象。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="assetType">目标资产类型。</param>
            /// <param name="callback">异步加载完成时的回调函数。</param>
            /// <param name="mode">访问模式。</param>
            public void LoadAsync(
                string assetName,
                System.Type assetType,
                OnAssetObject<Object> callback = null,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                {
                    End();
                    return;
                }

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    Object o = ResourcesManager.Instance.GetResouceObject(info.AssetPath[0], assetType);
                    if (o != null)
                    {
                        End(o);
                        return;
                    }
                    ResourcesManager.Instance.LoadAsync(info.AssetPath[0], assetType, callback);
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        Object o = UnityEditor.AssetDatabase.LoadAssetAtPath(info.AssetPath[0], assetType);
                        End(o);
                        return;
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null ||
                        ab.AssetBundleContent == null)
                    {
                        AssetBundleManager.Instance.LoadAsync(assetName, info, (b) => {
                            End(AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType));
                        });
                        return;
                    }
                    else
                    {
                        Object o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType);
                        if (o != null)
                        {
                            End(o);
                            return;
                        }
            
                        ab.Expand();
                        End(AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName, assetType));
                    }
                }

                void End(Object o = null)
                {
                    callback?.Invoke(o);
                }
            }
            
            /// <summary>
            /// 异步加载资产对象。
            /// </summary>
            /// <param name="assetName">资产路径字符串。</param>
            /// <param name="callback">异步加载完成时的回调函数。</param>
            /// <param name="mode">访问模式。</param>
            public void LoadAsync(
                string assetName,
                OnAssetObject<Object> callback = null,
                AssetAccessMode mode = AssetAccessMode.UNKNOWN)
            {
                AssetInfo info = GetAssetInfo(assetName, mode);
                if (!IsLegal(ref info))
                {
                    End();
                    return;
                }

                if (info.AssetType == AssetTypeEnum.RESOURCE)
                {
                    Object o = ResourcesManager.Instance.GetResouceObject(info.AssetPath[0]);
                    if (o != null)
                    {
                        End(o);
                        return;
                    }
                    ResourcesManager.Instance.LoadAsync(info.AssetPath[0], callback);
                }
                else if (info.AssetType == AssetTypeEnum.ASSET_BUNDLE)
                {
#if UNITY_EDITOR
                    if (_isEditorMode)
                    {
                        Object o = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(info.AssetPath[0]);
                        End(o);
                        return;
                    }
#endif
                    AssetBundleLoader ab = AssetBundleManager.Instance.GetAssetBundleLoader(info.AssetBundlePath);
                    if (ab == null ||
                        ab.AssetBundleContent == null)
                    {
                        AssetBundleManager.Instance.LoadAsync(assetName, info, (b) => {
                            End(AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName));
                        });
                        return;
                    }
                    else
                    {
                        Object o = AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName);
                        if (o != null)
                        {
                            End(o);
                            return;
                        }
            
                        ab.Expand();
                        End(AssetBundleManager.Instance.GetAssetObject(info.AssetBundlePath, info.AbName));
                    }
                }

                void End(Object o = null)
                {
                    callback?.Invoke(o);
                }
            }
            
            
            private AssetInfo GetAssetInfoFromResource(string assetName, bool showTip = false)
            {
                if (ResourceMap.Mappings.TryGetValue(assetName, out string value))
                {
                    return new AssetInfo(AssetTypeEnum.RESOURCE, new []{value}, null, null);
                }

                if (showTip)
                {
                    LogF8.LogError("Resource都找不到指定资源可用的索引：" + assetName);
                }
                return new AssetInfo();
            }
            
            private AssetInfo GetAssetInfoFromAssetBundle(string assetName, bool remote = false, bool showTip = false)
            {
                if (AssetBundleMap.Mappings.TryGetValue(assetName, out AssetBundleMap.AssetMapping assetmpping))
                {
                    if (remote || ForceRemoteAssetBundle)
                    {
                        return new AssetInfo(AssetTypeEnum.ASSET_BUNDLE, assetmpping.AssetPath, AssetBundleManager.GetRemoteAssetBundleCompletePath(), assetmpping.AbName);
                    }
                    else
                    {
                        return new AssetInfo(AssetTypeEnum.ASSET_BUNDLE, assetmpping.AssetPath, AssetBundleManager.GetAssetBundlePathWithoutAb(assetName), assetmpping.AbName);
                    }
                }

                if (showTip)
                {
                    LogF8.LogError("AssetBundle都找不到指定资源可用的索引：" + assetName);
                }
                return new AssetInfo();
            }
            
            /// <summary>
            /// 通过资源名称同步卸载。
            /// </summary>
            /// <param name="assetName">资源名称。</param>
            /// <param name="unloadAllLoadedObjects">
            /// 完全卸载。
            /// </param>
            public void Unload(string assetName, bool unloadAllLoadedObjects = false)
            {
#if UNITY_EDITOR
                if (_isEditorMode)
                {
                    return;
                }
#endif
                AssetInfo ab = GetAssetInfoFromAssetBundle(assetName);
                if (IsLegal(ref ab))
                {
                    AssetBundleManager.Instance.Unload(ab.AssetBundlePath, unloadAllLoadedObjects);
                }
                AssetInfo abRemote = GetAssetInfoFromAssetBundle(assetName, true);
                if (IsLegal(ref abRemote))
                {
                    AssetBundleManager.Instance.Unload(abRemote.AssetBundlePath, unloadAllLoadedObjects);
                }
                AssetInfo res = GetAssetInfoFromResource(assetName);
                if (IsLegal(ref res))
                {
                    ResourcesManager.Instance.Unload(res.AssetPath[0]);
                }
            }
            
            /// <summary>
            /// 通过资源名称异步卸载。
            /// </summary>
            /// <param name="assetName">资源名称。</param>
            /// <param name="unloadAllLoadedObjects">
            /// 完全卸载。
            /// </param>
            /// <param name="callback">异步卸载完成时的回调函数。</param>
            public void UnloadAsync(string assetName, bool unloadAllLoadedObjects = false, AssetBundleLoader.OnUnloadFinished callback = null)
            {
#if UNITY_EDITOR
                if (_isEditorMode)
                {
                    return;
                }
#endif
                AssetInfo ab = GetAssetInfoFromAssetBundle(assetName);
                if (IsLegal(ref ab))
                {
                    AssetBundleManager.Instance.UnloadAsync(ab.AssetBundlePath, unloadAllLoadedObjects, callback);
                }
                AssetInfo abRemote = GetAssetInfoFromAssetBundle(assetName, true);
                if (IsLegal(ref abRemote))
                {
                    AssetBundleManager.Instance.UnloadAsync(abRemote.AssetBundlePath, unloadAllLoadedObjects, callback);
                }
            }
            
            /// <summary>
            /// 通过资源名称获取加载器的加载进度。
            /// 正常值范围从 0 到 1。
            /// 但如果没有加载器，则返回 -1。
            /// </summary>
            /// <param name="assetName">资源名称。</param>
            /// <returns>加载进度。</returns>
            public float GetLoadProgress(string assetName)
            {
#if UNITY_EDITOR
                if (_isEditorMode)
                {
                    return 1f;
                }
#endif
                float progress = 2.1f;

                string assetBundlePath = "";
                string assetBundlePathRemote = "";
                
                AssetInfo ab = GetAssetInfoFromAssetBundle(assetName);
                if (IsLegal(ref ab))
                {
                    assetBundlePath = ab.AssetBundlePath;
                }

                AssetInfo abRemote = GetAssetInfoFromAssetBundle(assetName, true);
                if (IsLegal(ref abRemote))
                {
                    assetBundlePathRemote = abRemote.AssetBundlePath;
                }

                AssetInfo res = GetAssetInfoFromResource(assetName);
                if (IsLegal(ref res))
                {
                    float resProgress = ResourcesManager.Instance.GetLoadProgress(res.AssetPath[0]);
                    if (resProgress > -1f)
                    {
                        progress = Mathf.Min(progress, resProgress);
                    }
                }
                
                float bundleProgress = AssetBundleManager.Instance.GetLoadProgress(assetBundlePath);
                if (bundleProgress > -1f)
                {
                    progress = Mathf.Min(progress, bundleProgress);
                }
                
                float bundleProgressRemote = AssetBundleManager.Instance.GetLoadProgress(assetBundlePathRemote);
                if (bundleProgressRemote > -1f)
                {
                    progress = Mathf.Min(progress, bundleProgressRemote);
                }

                if (progress >= 2f)
                {
                    progress = 0f;
                }

                return progress;
            }
            
            /// <summary>
            /// 获取所有加载器的加载进度。
            /// 正常值范围从 0 到 1。
            /// 但如果没有加载器，则返回 -1。
            /// </summary>
            /// <returns>加载进度。</returns>
            public float GetLoadProgress()
            {
#if UNITY_EDITOR
                if (_isEditorMode)
                {
                    return 1f;
                }
#endif
                float progress = 2.1f;
                float abProgress = AssetBundleManager.Instance.GetLoadProgress();
                if (abProgress > -1f)
                {
                    progress = Mathf.Min(progress, abProgress);
                }
                float resProgress = ResourcesManager.Instance.GetLoadProgress();
                if (resProgress > -1f)
                {
                    progress = Mathf.Min(progress, resProgress);
                }
                if (progress >= 2f)
                {
                    progress = 0f;
                }
                return progress;
            }
            
            public void OnInit(object createParam)
            {
                _assetBundleManager = ModuleCenter.CreateModule<AssetBundleManager>();
                _resourcesManager = ModuleCenter.CreateModule<ResourcesManager>();
                if (File.Exists(Application.persistentDataPath + "/" + nameof(AssetBundleMap) + ".json"))
                {
                    string json =
                        FileTools.SafeReadAllText(Application.persistentDataPath + "/" + nameof(AssetBundleMap) + ".json");
                    AssetBundleMap.Mappings = Util.LitJson.ToObject<Dictionary<string, AssetBundleMap.AssetMapping>>(json);
                }
                else
                {
                    AssetBundleMap.Mappings = Util.LitJson.ToObject<Dictionary<string, AssetBundleMap.AssetMapping>>(Resources.Load<TextAsset>(nameof(AssetBundleMap)).ToString());
                }
                ResourceMap.Mappings = Util.LitJson.ToObject<Dictionary<string, string>>(Resources.Load<TextAsset>(nameof(ResourceMap)).ToString());
            }

            public void OnUpdate()
            {
                
            }

            public void OnLateUpdate()
            {
                
            }

            public void OnFixedUpdate()
            {
                
            }

            public void OnTermination()
            {
                base.Destroy();
            }
    }
}