# 版权与第三方声明

宝哥工具箱（ToolsBox）

Copyright (c) 2026 tydybb

项目原创程序代码按 **GNU General Public License v3.0 only（GPL-3.0-only）** 提供，完整条款见 [LICENSE](LICENSE)。分发修改版或二进制时，请履行 GPL 对相应源代码、许可与声明的要求；本文件没有增加任何 GPL 链接例外。

## 第三方组件

第三方组件保留其原作者版权，继续适用各自的许可和声明；项目代码的 GPL 声明不改变这些条款。完整版本清单、来源与许可原文见 [docs/third-party](docs/third-party/README.md)。其中包括：

- Microsoft.Data.Sqlite、TraceEvent 及其 Microsoft/.NET 传递依赖：MIT；包版权声明为 `© Microsoft Corporation. All rights reserved.`，上游许可同时保留 `.NET Foundation and Contributors` 声明。
- SQLitePCLRaw 的 bundle、core、provider 2.1.6：Apache-2.0，`Copyright 2014-2023 SourceGear, LLC`；原生库包 2.1.13：Apache-2.0，`Copyright 2014-2024 SourceGear, LLC`。
- Microsoft.Web.WebView2 SDK 1.0.4191.47：包内 Microsoft BSD 三条款形式许可及其 NOTICE；这份 SDK 许可与单独安装的 WebView2 Runtime 许可应分别处理。
- 内置 .NET 的发行包还包含 .NET Runtime / Windows Desktop Runtime；其主许可为 MIT，相关第三方组件声明随附于本目录。当前许可清单对应 8.0.30。

发布 ZIP 应同时包含程序、根目录 `LICENSE`、本 `NOTICE.md` 及完整 `docs/third-party` 目录。更新依赖或运行时版本后，应同步核对并更新这些文本。

## 图像与品牌

当前公开构建以原创几何图形替代先前参考第三方角色的倒计时素材；未获再分发授权的旧角色 PNG 不纳入当前公开发布包。本项目不声称获得角色原作者的官方授权、联名或认可。

Git 历史保持原状，不重写历史。历史提交中存在某项素材，不表示该素材获得了本项目的 GPL 授权或可自由再分发；其相关权利仍由原权利人保留。

GPL 的程序代码许可不授予项目品牌、商标或第三方角色标识的使用权，也不表示相关权利人对本项目背书。

## 单独下载的组件

发行包不捆绑 WebView2 Runtime、yt-dlp、FFmpeg 或 ffprobe。用户通过工具中的安装入口或自行安装这些组件时，应另行阅读对应版本、构建配置与依赖的许可；这些组件不会因被本程序调用而自动改用本项目的 GPL 声明。

网页与媒体功能不提供所有网站、账号、播放器或 DRM 兼容保证，也不绕过网站访问权限。用户应确认自己有权访问及下载相应内容。
