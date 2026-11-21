// AI-perf-saves:存档回滚的「先备份当前状态再回滚」封装(复用 Saves 的 BackupSave / RestoreBackup)。
// i18n:ignore-file —— 本文件的中文只在异常信息里(与 Saves.cs 同口径),由工具结果交给模型转述。
//
// Saves.RestoreBackup 本身只管「解包 + 换目录」,覆盖时把旧存档挪到临时目录、成功后**删掉**——
// 也就是说直接调它覆盖回滚,回滚前的那份存档就没了。界面(SavesDialog)与 AI 工具都应该先把当前
// 同名存档打一份 zip,这里把两步收成一个入口,并把「会落到哪个目录」提前算好(确认卡预览与检查点
// 要在执行前就知道目标路径,算法必须与执行时一致,所以执行时把算好的名字显式传给 RestoreBackup)。
using System;
using System.Collections.Generic;
using System.IO;

namespace TipCL.Core.Launcher;

/// <summary>存档回滚计划与带安全备份的回滚。</summary>
public static class SaveBackups
{
    private static string GameDir(Instance instance, object? version)
        => Py.Truthy(version) ? VersionSettings.GameDir(instance, version) : instance.path;

    /// <summary>名字只能是目录下的一级子项(不许穿路径),与 Saves._safe_child 同规则。</summary>
    private static string SafeChild(string folder, string name, string what)
    {
        PPath root = new PPath(folder).Resolve();
        PPath child = (root / name).Resolve();
        if (name.Length == 0 || !child.Parent.Equals(root))
        {
            throw new SaveError(Py.Fmt("非法{0}名: {1}", what, name));
        }
        return child.ToString();
    }

    /// <summary>
    /// 算出这次回滚会做什么(纯读,不改盘):
    /// <c>{backup, backup_path, backup_exists, world, current_path, current_exists, target, target_path,
    /// overwrite, will_backup_current}</c>。
    /// world = 备份文件名去掉时间戳(与 Saves.ListBackups 的 save 字段同算法);
    /// overwrite=false 且同名存档已存在时目标改成「world-还原」「world-还原2」…(与 RestoreBackup 同算法)。
    /// </summary>
    public static PyDict PlanRestore(Instance instance, string backupName, object? version, bool overwrite)
    {
        string backups = Saves.BackupsDir(instance, version);
        string backupPath = SafeChild(backups, backupName ?? "", "备份");
        string saves = PPath.Join(GameDir(instance, version), "saves");
        string world = Saves.StripStamp(new PPath(backupPath).Stem);
        string current = SafeChild(saves, world, "存档");
        bool currentExists = LocalFs.Exists(current);
        string target = world;
        string targetPath = current;
        if (!overwrite)
        {
            int n = 1;
            while (LocalFs.Exists(targetPath))
            {
                target = world + "-还原" + (n > 1 ? n.ToString() : "");
                targetPath = SafeChild(saves, target, "存档");
                n++;
            }
        }
        return PyDict.Of(
            "backup", new PPath(backupPath).Name,
            "backup_path", backupPath,
            "backup_exists", LocalFs.IsFile(backupPath),
            "world", world,
            "current_path", current,
            "current_exists", currentExists && LocalFs.IsDir(current),
            "target", target,
            "target_path", targetPath,
            "overwrite", overwrite,
            "will_backup_current", currentExists && LocalFs.IsDir(current));
    }

    /// <summary>
    /// 先备份、再回滚:同名存档存在时**一律**先用 <see cref="Saves.BackupSave"/> 打一份 zip(覆盖与否都打 ——
    /// 回滚前留一份当前状态是硬规矩,不赌「这次不覆盖所以不用备」),备份失败就不回滚;
    /// 然后把 <see cref="PlanRestore"/> 算出的目标名显式交给 <see cref="Saves.RestoreBackup"/>。
    /// 返回 <c>{name, path, from, overwrite, safety_backup, safety_backup_path}</c>(safety_backup 为空 = 当时没有同名存档)。
    /// </summary>
    public static PyDict RestoreWithSafetyBackup(Instance instance, string backupName, object? version, bool overwrite,
        Action<string, long, long>? onProgress = null)
    {
        PyDict plan = PlanRestore(instance, backupName, version, overwrite);
        if (!Py.Truthy(plan.Get("backup_exists")))
        {
            throw new SaveError(Py.Fmt("备份不存在: {0}", backupName));
        }
        string safetyName = "";
        string safetyPath = "";
        if (Py.Truthy(plan.Get("will_backup_current")))
        {
            PyDict safety = Saves.BackupSave(instance, plan.Get("world"), version, onProgress);
            safetyName = Py.S(safety.Get("name"));
            safetyPath = Py.S(safety.Get("path"));
            if (safetyName.Length == 0 || !LocalFs.IsFile(safetyPath))
            {
                throw new SaveError("回滚前备份当前存档失败，已取消回滚");
            }
        }
        PyDict restored = Saves.RestoreBackup(instance, plan.Get("backup"), version, plan.Get("target"), overwrite);
        return PyDict.Of(
            "name", restored.Get("name"),
            "path", restored.Get("path"),
            "from", restored.Get("from"),
            "overwrite", overwrite,
            "safety_backup", safetyName,
            "safety_backup_path", safetyPath);
    }
}
