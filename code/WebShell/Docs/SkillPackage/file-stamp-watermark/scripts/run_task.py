# -*- coding: utf-8 -*-
"""run_task.py —— 校验任务 JSON 并调用软件 EXE 执行批处理。

用法:
    python run_task.py "<软件文件夹路径>" "<任务.json路径>" "[日志路径]"

内部执行:
    "<EXE>" --batch "<任务.json>" --log "<日志路径>"

输出（机器可读，供智能体解析）:
    RESULT: <exit_code>
    <日志末尾「===== 给用户的反馈 =====」段落原文（若存在）>
    （找不到 EXE 时输出 EXE_NOT_FOUND；JSON 校验失败输出对应错误行）

退出码含义（来自软件）:
    0 = 全部成功；1 = 参数或路径错误（任务未执行）；2 = 部分文件失败（明细见日志）。
仅用 Python 标准库，零第三方依赖。
"""
import json
import os
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from find_exe import find_exe  # noqa: E402

MANDATORY = {
    'pdf-stamp': ['input', 'stamp.image'],
    'pdf-watermark': ['input', 'watermark.text'],
    'pdf-text-stamp': ['input', 'stamp.image', 'textStamp.keyword'],
    'img-watermark': ['input', 'watermark.text'],
}


def _validate(task_path):
    """返回 (ok, err)。校验存在、可解析、必填字段。"""
    if not os.path.isfile(task_path):
        return False, '任务文件不存在: %s' % task_path
    try:
        with open(task_path, 'rb') as f:
            raw = f.read()
        for enc in ('utf-8-sig', 'utf-8', 'gbk'):
            try:
                text = raw.decode(enc)
                break
            except (UnicodeDecodeError, LookupError):
                continue
        else:
            text = raw.decode('utf-8', errors='replace')
        data = json.loads(text)
    except Exception as e:
        return False, '任务 JSON 解析失败: %s' % e
    task = data.get('task')
    if task not in MANDATORY:
        return False, '缺少 task 或 task 不支持: %s' % task
    if not data.get('input'):
        return False, '缺少 input'
    for field in MANDATORY[task]:
        parts = field.split('.')
        node = data
        ok = True
        for part in parts:
            if not isinstance(node, dict) or part not in node:
                ok = False
                break
            node = node[part]
        if not ok or node in (None, '', []):
            return False, '缺少必填字段: %s' % field
    return True, ''


def main():
    if len(sys.argv) < 3:
        print('用法: python run_task.py "<软件文件夹路径>" "<任务.json路径>" "[日志路径]"')
        sys.exit(1)
    root = sys.argv[1].strip().strip('"')
    task_path = sys.argv[2].strip().strip('"')
    log_path = sys.argv[3].strip().strip('"') if len(sys.argv) > 3 else ''

    exe = find_exe(root)
    if not exe:
        print('EXE_NOT_FOUND')
        sys.exit(1)

    ok, err = _validate(task_path)
    if not ok:
        print(err)
        sys.exit(1)

    if not log_path:
        work = os.path.join(root, '运行组件', '智能体调用', '智能体调用工作过程文件')
        os.makedirs(work, exist_ok=True)
        # 默认日志用时间戳命名，避免多任务互相覆盖、读到旧日志反馈段
        log_path = os.path.join(work, '日志_任务_%s.txt' % time.strftime('%H%M%S'))

    cmd = [exe, '--batch', task_path, '--log', log_path]
    try:
        proc = subprocess.run(
            cmd, capture_output=True, text=True, timeout=1800,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0,
        )
    except subprocess.TimeoutExpired:
        print('RESULT: timeout')
        sys.exit(1)
    except Exception as e:
        print('执行失败: %s' % e)
        sys.exit(1)

    print('RESULT: %s' % proc.returncode)
    # 参数/路径错误（退出码1）：输出 exe 标准输出的错误信息；日志可能是旧内容，不读取反馈段
    if proc.returncode != 0:
        out = (proc.stdout or '').strip()
        if out:
            print(out)
        return
    if os.path.isfile(log_path):
        with open(log_path, 'rb') as f:
            raw = f.read()
        for enc in ('utf-8-sig', 'utf-8', 'gbk'):
            try:
                log_text = raw.decode(enc)
                break
            except (UnicodeDecodeError, LookupError):
                continue
        else:
            log_text = raw.decode('utf-8', errors='replace')
        marker = '===== 给用户的反馈 ====='
        idx = log_text.find(marker)
        if idx >= 0:
            print(log_text[idx + len(marker):].strip())


if __name__ == '__main__':
    main()
