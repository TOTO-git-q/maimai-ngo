maimai NEEDY GIRL OVERDOSE 换皮 - 安装包（组内私用，请勿外传）
================================================================

适用：你自己的 maimai DX SDEZ 1.70（MAGiCAL）本机版 + MelonLoader + AquaMai。
本包不含任何游戏本体文件：安装器读你自己的游戏文件，把换皮的贴图、颜色、场景改动写进去。

安装
  1. 关掉游戏。
  2. 双击 install.bat（或把你的 Package 文件夹拖到 install.bat 上）。
     找不到游戏时会让你把 Package 文件夹（有 Sinmai.exe 的那个）拖进窗口再回车。
  3. 安装前会把所有会被改的原文件备份到 Package\NGO_skin_backup\。
     再次安装（比如更新到新版本）会先从这个备份还原再重新写入，可以放心重复运行。
  4. 本包不带搭档角色素材。自定义搭档放进 LocalAssets\PartnerLive2D\<名字>\partner.ini（见仓库说明）。
     Live2DCubismCore.dll 是可选的：只有 Live2D 模型做的搭档需要，换皮本身不需要它。
     （要用 Live2D 模型搭档的话：去 Live2D 官网下载 Cubism SDK for Native，
      把 Core\dll\windows\x86_64\Live2DCubismCore.dll 放到 Sinmai.exe 旁边。）

卸载
  双击 uninstall.bat：从备份放回原版文件，删掉换皮新增的文件。

查看状态
  双击 status.bat。

包里有什么
  ngo_patcher\     安装器（不需要装 Python）
  patch\           换皮数据：textures\ 贴图 PNG、targets\ 每个游戏文件要改的对象和字段、
                   xml_patch.json 曲目分类配色、files\ 模组和它的素材、exact\ 参考机的精确压缩数据
  install.bat / uninstall.bat / status.bat

不会动的东西
  segatools.ini、DEVICE\aime.txt、AquaMai.toml、存档、日志一概不碰。
  settings.ini 里会改账号的选项（charaLevel 等）在这个包里默认是关的。

----------------------------------------------------------------
English: close the game, double-click install.bat (or drop your Package folder on it).
Originals are backed up to Package\NGO_skin_backup; re-running is safe; uninstall.bat restores them.
No partner character art is included. Live2DCubismCore.dll is optional (only Live2D-model partners need it).
Private collaboration build: do not redistribute.
