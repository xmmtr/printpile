# PrintPile · 批量打印

一次拖拽，几百份文档排队打完——Windows 桌面批量打印工具。PDF / Word / Excel / PPT / 图片 / 文本混批排队、逐文件设份数与页码范围，打印前双击即可逐页预览；全程本机处理，文档永不上传。

**PrintPile 是完全免费的开源软件**，以 [GNU GPLv3](LICENSE) 许可证发布：全部功能对所有用户开放，无任何数量限制。

## 功能亮点（全部免费、无限量）

- **全格式混批**：PDF / Word / Excel / PPT / 图片 / 文本统一排队，Office 文档后台自动转换，全程零弹窗
- **双击预览＋版面体检**：打印前逐页预览，内容超出可打印区域的部分直接红纹标注，贴边与超宽提前提醒
- **打印模板不限量**：常用参数组合存成模板一键套用，内置四枚默认模板
- **逐文件参数**：份数、页码范围在列表行里直接填；双面、纸张、缩放、每页版数、小册子全套支持
- **打印历史不限量＋CSV 导出**：自动留档可筛选，一键导出 CSV 台账
- **监视文件夹自动打印**：指定文件夹，新文件落进去自动按规则打印，适合扫描件、单据流程
- **隐私优先**：仅本机打印，不上传任何文档内容

## 官网与在线工具

- 官网：<https://printpile.cn>
- 在线 PDF 工具（浏览器本地处理，文件不上传）：<https://printpile.cn/site-tools.html>
- 问题反馈：<https://printpile.cn/feedback.html>

## 下载安装

普通用户请从官网或本仓库 **Releases** 页获取安装包 / 便携版：

- 安装包（Setup）：双击安装，自带卸载器
- 便携版（portable.zip）：解压即用，不写注册表

## 从源码构建

依赖：**.NET 8 SDK**（Windows 10/11 x64）。Office 文档转换依赖本机自备的 Microsoft Office 或 WPS。

```bat
:: 还原并构建整个解决方案
dotnet build BatchPrintCN.sln -c Release

:: 主程序（单文件发布）
dotnet publish BatchPrintCN-WPF\BatchPrintCN-WPF.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out

:: 后台转换子进程（发布到 out\bin，与主程序 exe 同级）
dotnet publish BatchPrintCN-Worker\BatchPrintCN-Worker.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out\bin
```

`BatchPrintCN.Core\libs\pdfium.dll` 为 PDF 渲染原生库（取自 PDFium，Apache-2.0 许可证），仓库内已附带，构建时作为内容文件参与输出。

## 目录结构

```
BatchPrintCN.sln              # 解决方案
Directory.Build.props         # 版本号等统一构建属性
BatchPrintCN-WPF/             # 主程序（WPF，.NET 8；NOTICE 版权说明在本目录内）
BatchPrintCN-Worker/          # 后台文档转换子进程
BatchPrintCN.Core/            # 打印/排版/导出核心库
LICENSE                       # GNU GPLv3 全文
```

## 许可证

本软件以 [GNU General Public License v3.0](LICENSE) 发布：你可以自由地使用、学习、修改和再分发，分发时请遵循 GPLv3 的相应义务（如随附许可证文本并提供相应源码）。

第三方组件：[PDFium](https://pdfium.googlesource.com/pdfium/)（Apache-2.0，见 `BatchPrintCN.Core\libs\pdfium.dll`）。
