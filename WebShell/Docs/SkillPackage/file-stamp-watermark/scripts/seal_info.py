# -*- coding: utf-8 -*-
"""seal_info.py —— 读取软件配置与印章参数（只读，不修改 config.ini）。

用法:
    python seal_info.py "<软件文件夹路径>"

输出:
    EXE --stamp-info 的 UTF-8 JSON：
    - tool / version：软件名与版本
    - config：config.ini 全部节与键值（原样，含全局配置与每章参数节）
    - seals[]：印章清单 + 每章已保存参数
        （sizeMm / rotation / rotationHandle / opacity / randomParams / randomRange /
         randomOffsetXMm / randomOffsetYMm / removeWhite / tolerance / maxSplit /
         textureQuality / textureBrightness / textureBlob / textureGradient /
         textureWhite / textureSpot / textureRadial / textureCast / texturePresetIndex）
    - 失败时 {"ok":false,"error":"原因"}

铁律：本脚本只读 config.ini（经 EXE --stamp-info 转 UTF-8 输出），绝不修改——软件配置由软件界面维护。
仅用 Python 标准库，零第三方依赖，跨平台智能体可跑。
"""
import os
import subprocess
import sys

from find_exe import find_exe


def main():
    if len(sys.argv) < 2:
        print('用法: python seal_info.py "<软件文件夹路径>"')
        sys.exit(1)
    root = sys.argv[1].strip().strip('"')
    exe = find_exe(root)
    if not exe:
        print('{"ok":false,"error":"未找到软件 EXE（请先双击运行一次软件）"}')
        sys.exit(1)
    try:
        out = subprocess.run(
            [exe, '--stamp-info'], capture_output=True, timeout=60,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0,
        )
    except Exception as e:
        print('{"ok":false,"error":"调用失败：%s"}' % str(e).replace('"', "'"))
        sys.exit(1)
    text = (out.stdout or b'').decode('utf-8', errors='replace').strip()
    if not text:
        print('{"ok":false,"error":"--stamp-info 无输出（stderr: %s）"}' %
              (out.stderr or b'').decode('utf-8', errors='replace')[:200])
        sys.exit(1)
    print(text)
    sys.exit(0 if out.returncode == 0 else 1)


if __name__ == '__main__':
    main()
