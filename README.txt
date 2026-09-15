Variable Flea Prices

一个用于 SPT 的跳蚤市场价格调整 Mod。
支持spt5.0.0 BE 260910

原本自带的价格过于离谱，最为经常玩pvp的玩家看着难受

本项目基于：
https://github.com/DrakiaXYZ/SPT-LiveFleaPrices-CSharp
原作者：DrakiaXYZ

原项目主要通过在线价格表获取跳蚤市场价格，这样做虽然很准确地贴合了pvp价格，但是每次启动都需要链接github。

本项目在原有基础上进行了修改：
将原本在线获取的价格表改为本地计算，具体价格调整，可以更改config中的config.json
现在所有10次次数的钥匙卡全部回归无限，包括破冰船的3张
关闭spt原本的价格乘积，和低于商人价格保护。
由于 Mod 不再通过联网获取外部价格表，而是根据本地配置的规则和公式计算跳蚤市场价格，所以维护本地价格将依靠config中的config.json。

目前规则仍然不够完善，暂时照顾了实验室钥匙卡和全部的手雷。
目前新武器比如nl545和马林并不支持修改，原因是接口的分类里没有这写条目，需要等待spt服务端的更新。

可以按照以下方式匹配物品：

itemTpl：按照物品 ID 精确匹配。
baseClass：按照物品的基础类别匹配。
支持同时指定多个 baseClass。

安装
将 Mod 解压到 .\SPT_Runtime\user\mods 目录下。

致谢

感谢 DrakiaXYZ 创建原始的 SPT-LiveFleaPrices-CSharp 项目。

本项目的部分代码基于原项目修改而来。
