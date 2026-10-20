using System;
using System.IO;
using System.Reflection;

namespace PDFQFZ.WebShell.Services
{
    /// <summary>
    /// V374：随软件分发的说明文档（智能体调用手册 / 智能体用户使用说明）嵌入 EXE 资源，
    /// 每次启动时覆盖写出到软件目录——出厂配置，用户不改，保证内容随版本自动同步。
    /// V375：任务 JSON 模板（4 类任务）嵌入 EXE，启动生成——智能体直接读取模板、
    /// 替换 input 与参数后另存调用，无需从零编写 JSON。
    /// V379：目录整理——给用户看的《智能体用户使用说明》留在软件根目录；给智能体看的
    /// 《智能体调用手册》与《智能体调用任务模板》统一收纳到 运行组件\智能体调用\ 子目录
    /// （根目录不再生成 智能体调用手册.md / 任务模板\ 旧文件，启动时自动清理旧位置出厂文件）。
    /// GUI 与命令行批处理模式均执行（智能体场景同样需要目录内有手册与模板）。
    /// </summary>
    public static class DocsBootstrap
    {
        // 输出相对路径, 资源名（csproj EmbeddedResource 编译，LogicalName=命名空间.路径.文件名）
        private static readonly string[][] Docs = new[]
        {
            new[] { "智能体用户使用说明.md", "PDFQFZ.WebShell.Docs.user_guide.md" },
            // V379：手册与模板统一在 运行组件\智能体调用\（运行组件目录由 AppConfig 启动时确保存在）
            new[] { @"运行组件\智能体调用\智能体调用手册.md", "PDFQFZ.WebShell.Docs.agent_handbook.md" },
            new[] { @"运行组件\智能体调用\智能体调用任务模板\pdf-stamp.json", "PDFQFZ.WebShell.Docs.Templates.pdf-stamp.json" },
            new[] { @"运行组件\智能体调用\智能体调用任务模板\pdf-watermark.json", "PDFQFZ.WebShell.Docs.Templates.pdf-watermark.json" },
            new[] { @"运行组件\智能体调用\智能体调用任务模板\pdf-text-stamp.json", "PDFQFZ.WebShell.Docs.Templates.pdf-text-stamp.json" },
            new[] { @"运行组件\智能体调用\智能体调用任务模板\img-watermark.json", "PDFQFZ.WebShell.Docs.Templates.img-watermark.json" },
        };

        /// <summary>V1.0.0.27：智能体调用技能包（file-stamp-watermark）文件清单——启动时释放到 运行组件\智能体调用\file-stamp-watermark\，
        /// 用户无需单独下载/安装技能包，运行一次 EXE 即自动就位（出厂配置，每次启动覆盖）。</summary>
        private static readonly string[][] SkillPackage = new[]
        {
            new[] { @"运行组件\智能体调用\file-stamp-watermark\SKILL.md", "PDFQFZ.WebShell.Docs.SkillPackage.SKILL.md" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\references\task-spec.md", "PDFQFZ.WebShell.Docs.SkillPackage.references.task-spec.md" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\references\workflows.md", "PDFQFZ.WebShell.Docs.SkillPackage.references.workflows.md" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\references\boundary.md", "PDFQFZ.WebShell.Docs.SkillPackage.references.boundary.md" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\assets\pdf-stamp.json", "PDFQFZ.WebShell.Docs.SkillPackage.assets.pdf-stamp.json" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\assets\pdf-watermark.json", "PDFQFZ.WebShell.Docs.SkillPackage.assets.pdf-watermark.json" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\assets\pdf-text-stamp.json", "PDFQFZ.WebShell.Docs.SkillPackage.assets.pdf-text-stamp.json" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\assets\img-watermark.json", "PDFQFZ.WebShell.Docs.SkillPackage.assets.img-watermark.json" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\scripts\find_exe.py", "PDFQFZ.WebShell.Docs.SkillPackage.scripts.find_exe.py" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\scripts\list_seals.py", "PDFQFZ.WebShell.Docs.SkillPackage.scripts.list_seals.py" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\scripts\run_task.py", "PDFQFZ.WebShell.Docs.SkillPackage.scripts.run_task.py" },
            new[] { @"运行组件\智能体调用\file-stamp-watermark\scripts\seal_info.py", "PDFQFZ.WebShell.Docs.SkillPackage.scripts.seal_info.py" },
        };

        /// <summary>启动时调用：确保软件目录存在出厂说明文档与任务模板（每次覆盖写回）。</summary>
        public static void EnsureDocsGenerated()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                var asm = Assembly.GetExecutingAssembly();
                foreach (var d in Docs)
                {
                    string outPath = Path.Combine(dir, d[0]);
                    using (var s = asm.GetManifestResourceStream(d[1]))
                    {
                        if (s == null) continue; // 资源缺失不报错（防启动失败）
                        // 每次覆盖：出厂配置不可改；文件被占用时捕获忽略（不影响启动）
                        string sub = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(sub)) Directory.CreateDirectory(sub);
                        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            s.CopyTo(fs);
                        }
                    }
                }
                // V1.0.0.27：释放智能体调用技能包（file-stamp-watermark）——与手册同级的第二个入口
                foreach (var d in SkillPackage)
                {
                    string outPath = Path.Combine(dir, d[0]);
                    using (var s = asm.GetManifestResourceStream(d[1]))
                    {
                        if (s == null) continue;
                        string sub = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(sub)) Directory.CreateDirectory(sub);
                        // V1.0.0.31：技能包版本号运行时注入——SKILL.md 占位符 {{VERSION}} 替换为当前程序集版本
                        // （与 EXE --version 输出一致），版本唯一来源 = 程序集版本，发版不可能漏改技能包版本号
                        if (d[1].EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
                        {
                            string content;
                            using (var reader = new StreamReader(s, System.Text.Encoding.UTF8))
                            {
                                content = reader.ReadToEnd();
                            }
                            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                            string verText = ver == null ? "1.0.0.0" : ver.ToString(4);
                            content = content.Replace("{{VERSION}}", verText);
                            File.WriteAllText(outPath, content, new System.Text.UTF8Encoding(false));
                        }
                        else
                        {
                            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                            {
                                s.CopyTo(fs);
                            }
                        }
                    }
                }
            }
            catch
            {
                // 释放失败不影响软件启动
            }

            // V379：清理旧布局出厂文件（根目录 智能体调用手册.md / 任务模板\）——
            // 旧版本曾生成于此，新版本统一移入 运行组件\智能体调用\；删除前仅清理已知出厂生成物，
            // 绝不动 偏好文件.json 等用户/智能体数据。
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string oldMd = Path.Combine(dir, "智能体调用手册.md");
                if (File.Exists(oldMd)) File.Delete(oldMd);
                string oldTpl = Path.Combine(dir, "任务模板");
                if (Directory.Exists(oldTpl)) Directory.Delete(oldTpl, true);
            }
            catch
            {
                // 清理失败不影响软件启动（残留旧文件由用户自行删除）
            }
        }
    }
}
