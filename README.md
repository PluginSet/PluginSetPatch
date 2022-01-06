# 资源分包与加载插件

## 一、 设计目的
```PluginSet.Patch```程序集开发主要有两个阶段，对应了两个不同的设计目的。最初该程序集是用来管理资源的加载与构建。加载逻辑需要满足：

 1. 能够方便的管理AssetBundle资源及其依赖的加载与卸载
 2. 在没有热更时，能通过```ResourcesManager.Instance.Load```能加载到```Resources```目录中的资源；在有热更时，能通过相同的接口加载到热更资源中的相应资源
 3. 相应的热更下载逻辑

第二阶段的设计目的，是在上述基础上，可以将游戏资源分离为多个资源包，并能按需加载资源包中的内容。

## 二、 设计思路
经DEMO测试发现，Unity的AssetBundle中的资源，可以依赖Resources中的资源，而反之不能。AssetBundle除了可以依赖Resources中的资源外，还可以依赖其它AssetBundle中的资源，但是依赖关系需要```AssetBundleManifest```数据来获取。因此在处理热更资源时，可以把变更的```Resources```资源构建到特定名称的AssetBundle中，在热更后从AssetBundle中加载。在处理分包资源时，每个分包资源及其依赖项都单独完成一次构建，按需加载相应的AssetBundle与其中的资源。

## 三、 主要实现
### ```AssetBundleRef``` AssetBundle对象引用计数与加载
该类主要用于管理AssetBundle的依赖关系，在卸载```AssetBundleRef```时，会判断该```AssetBunldeRef```是否被其它AssetBundle引用依赖，如果无引用时，资源会被真正释放，否则时是引用计数减1。以此来确保被依赖的资源不会被提前释放。

除此外，该类还封装了同步加载与异步加载的方法，以及判断资源是否存在的方法。具体接口详见相关代码。 

### ```FileManifest``` 文件清单
在某一组资源包的AssetBundle构建完成后，资源包相关信息，包括```AssetBundleManifest```数据，以及所有文件（除AssetBundle外，可能还有某些会被直接使用的资源）的名称、修改后文件名（目前会在所有文件后增加文件相应的md5值）、文件大小、文件MD5值信息会被写入到FileManifest文件中。

通过```FileManifest```可以获取资源包中包含的所有文件信息，也可以判断AssetBundle是否存在，以及获取真实的文件名称。该类也替代AssetBundleManifest提供了获取AssetBundle所有依赖的接口。

### ```PatchUtil``` 通用方法
 1. ```CheckResourceVersion``` 比对资源版本号方法，资源版本号由APP版本号与Build号组成，格式为```v{VersionName}-{Build}```，其中VersionName为APP版本号，Build为构建生产线自增数，在Apk中也会用作```VersionCode```。VersionName格式为```1.2.3```，最多支持4位，每位支持0～99之间的数值。比对资源版本号时，如果VersionName比当前的高，则会返回```CheckResult.DownloadApp```，当VersionName相同但Build号比当前的高时，则会返回```CheckResult.DownloadPatches```，否则返回```CheckResult.Nothing```。

 2. ```CheckFileInfo``` 比对文件信息是否匹配。该方法会同步读取指定文件所有二进制数据，读取结果与传数的文件信息的长度、MD5作对比，如果相同则返回真。该方法为同步计算，如果文件较大或较多，该方法可能会有效率问题。

3. ```GetResourcesAssetBundle``` 将```Resources```文件目录名换算为AssetBundle名称与相应资源名称的方法。在资源构建与读取时会使用该方法。

4. ```CheckDownloadPatch``` 检测更新资源包的示例逻辑。该方法需要协程调用，传入的回调函数会接收下载器对象，通过下载器对象下载资源，如果不需要更新，则下载器为空。

### ```PatchesDownloader``` 资源包下载
```PatchDownloader```提供了根据```FileManifest```文件或其链接，检验本地文件md5值并下载```FileManifest```中缺失文件的方法。

 1. **准备下载** 开始下载前需要先调用```PrepareDownload```方法准备下载任务，该方法会根据传入的```FileManifest```或指定的```FileManifest```链接，遍历包中应包含的所有文件与其数据，是否与本地文件匹配，若文件不匹配则会创建一个文件下载任务。该方法需使用协程调用，结束后可获取该次下载所包含的下载任务个数与下载文件总大小
 2. **开始下载** 协程调用```Start```来开始下载任务，同时下载文件个数默认为5，可以在下载器创建时设定最大同时下载文件个数。下载失败的任务会被缓存到失败列表。
 3. **重试** 下载失败可以使用```Retry```接口重新下载，已下载成功的文件不会被再资下载。
 4. **下载完成** 下载完成后，如果该资源包中包含子包，会检测比对子包版本，如果该资源包中子包更新，则会替换本地子包；最后，保存该资源包对应的```FileManifest```。

### ```PatchResourcesManager``` 资源包资源管理器
```PatchResourcesManager```实现了```PluginSet.Core.ResourcesManager```中的资源加载接口，并有以下特性：
 1. 添加/移除搜索目录时，会加载/卸载相应的资源分包，加载资源时，会根据分包的顺序依次检查是否包含对应资源。
 2. ```Load```和```LoadAsync```接口会优先检查AssetBundle中是否包含相应的资源（AssetBundle名称和资源名称通过```PatchUtil.GetResourcesAssetBundle```获得），如果存在则加载AssetBundle中的相应资源，如果不存在则直接加载```Resources```中的资源。
 3. ```LoadBundle```会将相应的```AssetBundleRef```引用计数+1，当不使用该AssetBundle时，需要```ReleaseBundle```。
 4. 加载AssetBundle前会加载其所有依赖的AssetBundle，以确保资源初始化正确。卸载AssetBundle时会偿试卸载其所有依赖的AssetBundle。因此在使用AssetBundle时，可以用不关心它的依赖项。

### ```PluginResoucesInit``` ```ResourcesManager```实例初始化
该运行插件为最优先运行项，在开始运行时，会解压包内的StreamingAssets到可写目录中，并初始化```ResourcesManager```单例实例。在重启/关闭时，会释放所有已加载资源。

### ```PluginPatchUpdate``` 增量更新检查与下载
该插件会较早执行，在此运行之前，项目可能需要先监听相关的事件以作界面表现。

运行流程：
 1. 派发事件【开始热更】 ```SendNotification(PluginPatchConstants.NOTIFY_UPDATE_START)```
 2. 派发事件【检测热更】 ```NotifyAnyOne(PluginPatchConstants.NOTIFY_CHECK_UPDATE, context)``` 如果没有任何插件监听该事件，会跳过热更流程。监听该事件可以根据用护操作/网络请求等异步事件来选择执行```context.Confirm```（确认执行热更）或```context.Cancel```（取消热更，取消热更会根据配置判断是否退出APP），确认执行前，需要先设置热更检测结果。
 ```context.Data```设置为```CheckResult.DownloadApp```表示需要通过设置的```context.Set<string>(PluginPatchConstants.KEY_DOWNLOAD_APP_URL, url)```下载链接下载最新的APP；
 ```context.Data```设置为```CheckResult.DownloadPatches```表示需要通过设置的```context.Set<string>(PluginPatchConstants.KEY_UPDATE_PATCH_URL, url)```更新链接和```context.Set<string>(PluginPatchConstants.KEY_STREAMING_URL, stream)```stream文件名来下载热更新；
 如果```context.Data```设置为```CheckResult.NeedCheck```，则会调用```PatchUtil.CheckResourceVersion```方法，依据设置的```context.Set<string>(PluginPatchConstants.KEY_VERSION_STRING, version)```来判断是否需要下载APP或热更新。
 3. 如果需要下载APP，派发事件【请求下载APP】```NotifyAnyOne(PluginPatchConstants.NOTIFY_REQUEST_DOWNLOAD_APP, context)``` 如果没有监听该事件，则会直接下载APP，如果已监听，则可根据用户操作选择来执行```context.Confirm```（下载APP）或```context.Cancel```（取消下载，根据配置判断是否退出APP ）
 4. 如果需要下载热更资源，下载器准备就绪，且下载任务大于0时，派发事件【请求下载资源】 ```NotifyAnyOne(PluginPatchConstants.NOTIFY_REQUEST_DOWNLOAD_PATCHES, context)``` 如果没有监听该事件，则会直接下载热更资源，如果已监听，则可根据用户操作选择来执行```context.Confirm```（下载资源）或```context.Cancel```（取消下载，根据配置判断是否退出APP ）
 5. 下载热更中途下载失败时，派发事件【网络错误】 ```NotifyAnyOne(PluginPatchConstants.NOTIFY_NET_ERROR, context)``` 如果没有监听该事件，则会直接重新尝试下载热更资源，如果已监听，则可根据用户操作选择来执行```context.Confirm```（继续下载资源）或```context.Cancel```（取消下载，根据配置判断是否退出APP ）
 6. 热更下载结束后，派发事件【重启APP】 ```SendNotification(PluginConstants.NOTIFY_RESTART)``` ```PluginsManager```将会接收该消息并稍后重启APP。
 7. 流程结束，派发事件【更新完成】 ```SendNotification(PluginPatchConstants.NOTIFY_UPDATE_COMPLETE)```

 ### 构建资源包
 #### 构建配置
  1. ```DisablePatchUpdate``` 禁用热更插件，禁用后不会编译```PluginPatchUpdate```，可自行定义热更逻辑
  2. ```ContinueIfUpdateFail``` 更新取消或失败后是否能继续游戏，如果不勾选择失败或取消后会退出APP，否则可以继续游戏
  3. ```EnableLoadAssetWithName``` 如果勾选，则加载AssetBundle中的资源时可以直接使用资源名称加载资源，不会修改AssetBundle构建参数。使用文件名加载资源可能会遇到同一个AssetBundle中不同目录下有相同文件相同类型的资源，则无法确保能加载到正确的资源。
  4. ```EnableCopyToStreamWhenBuildUpdatePatches``` 如果勾选，在BuildPatches时，子包如果设置为拷贝到Stream目录，则其中的所有资源会拷贝到Stream目录，可以使子包包含在主包下载资源中。
  5. ```Patches``` 资源包设置。除主包（StreamingAssets)外，其它资源包都需在该属性中设置好它相应的参数，包括**资源包名称**、**是否拷贝到Stream目录中**、**资源包目录信息**，**资源包目录信息**中需包含所有子包对应的资源目录，勾选```UseResourceLoad```会将该目录中的资源，构建到名称规则同```Resources```的AssetBundle中，这样，该目录中的资源可以通过```ResourcesManager.Instance.Load```接口来加载。

#### 构建流程
* 在构建开始时，首先将各子资源包中所包含的资源按各自的规则加入相应的AssetBundle构建中。构建子包时，目前需考虑其它插件（tolua, fairyGUI）对资源的管理，该部分逻辑后续需要考虑如何解藕。在子包资源各自构建完成后，需拷贝至stream目录的拷贝到stream目录，其它资源包拷贝到BuildPatch中备用。
* 构建完成时（包括子包构建完成时），根据目标目录中的所有文件以及返回的```AssetBundleMenifest```数据写入```FileManifest```。所有文件都会被重新命名，目前的重命名规则为```{file_name}_{file_md5}.{extension}```，使文件内容与文件名称一一对应。