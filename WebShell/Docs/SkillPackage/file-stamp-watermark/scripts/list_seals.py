# -*- coding: utf-8 -*-
"""list_seals.py —— 列出软件印章库中的印章图片。

用法:
    python list_seals.py "<软件文件夹路径>"

输出:
    每个印章文件名一行（不含路径）；印章库不存在或为空时输出 NO_SEALS。
印章库位置: <软件文件夹> 下 运行组件/印章库/ 目录 （.png/.jpg）
仅用 Python 标准库，零第三方依赖。
"""
import os
import sys


def list_seals(root):
    if not root or not os.path.isdir(root):
        return []
    lib = os.path.join(root, '运行组件', '印章库')
    if not os.path.isdir(lib):
        return []
    out = []
    for name in os.listdir(lib):
        if name.lower().endswith(('.png', '.jpg', '.jpeg', '.bmp')):
            out.append(name)
    return out


def main():
    if len(sys.argv) < 2:
        print('用法: python list_seals.py "<软件文件夹路径>"')
        sys.exit(1)
    root = sys.argv[1].strip().strip('"')
    seals = list_seals(root)
    if not seals:
        print('NO_SEALS')
        return
    for s in seals:
        print(s)


if __name__ == '__main__':
    main()
