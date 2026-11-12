// 来源: Windows 版参考仓库 src/TipCL/Pages/AskBlock.cs + Pages/AskCard.cs(M4-H P5)。
// 结构化选择题:一道题一个块(单选 RadioButton / 多选 CheckBox),勾「其他」时露出补充输入框;
// 整卡由「确定 / 跳过」收尾,提交后冻结(照参考版的 Freeze)。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TipCL.App.Pages.Layout;
using TipCL.App.Ui;
using TipCL.App.Common;
using TipCL.Core;
using UiClass = TipCL.App.Ui.Ui;

namespace TipCL.App.Pages.Ai;

/// <summary>一道选择题(照 Windows 版 AskBlock)。</summary>
internal sealed class AskBlock
{
    private readonly List<(ToggleButton Button, string Id, string Label)> _opts = new();
    private readonly TextBox _other = UiClass.Input("选「其他」时在这里填");
    private readonly bool _multi;
    private readonly ToggleButton? _otherBtn;

    public string Id { get; }

    public string Prompt { get; }

    public StackPanel Root { get; }

    public AskBlock(PyDict question)
    {
        string id = Py.S(question.Get("id"));
        Id = id.Length > 0 ? id : "q1";
        string prompt = Py.S(question.Get("prompt"));
        Prompt = prompt.Length > 0 ? prompt : "请选择";
        _multi = Py.Truthy(question.Get("allow_multiple"));

        Root = UiClass.V(5, UiClass.Txt(Prompt + (_multi ? "(可多选)" : ""), 12.5).Wrap());
        string group = Guid.NewGuid().ToString("N");
        foreach (object? raw in Py.ListOr(question.Get("options")))
        {
            PyDict option = Py.D(raw);
            string optId = Py.S(option.Get("id"));
            string optLabel = Py.S(option.Get("label"));
            string label = optLabel.Length > 0 ? optLabel : optId;
            ToggleButton button = _multi
                ? new CheckBox { Content = label }
                : new RadioButton { Content = label, GroupName = group };
            button.Margin = new Thickness(0, 1, 0, 1);
            button.IsCheckedChanged += (_, _) => SyncOther();
            if (optId == "other")
            {
                _otherBtn = button;
            }
            _opts.Add((button, optId, label));
            Root.Children.Add(button);
        }
        _other.IsVisible = false;
        Root.Children.Add(_other);
    }

    private void SyncOther() => _other.IsVisible = _otherBtn is { IsChecked: true };

    /// <summary>收集本题选择;什么都没选(也没填其他)返回 null。</summary>
    public PyDict? Collect()
    {
        var picked = _opts
            .Where(o => o.Button.IsChecked == true)
            .Select(o => (object)PyDict.Of("id", o.Id, "label", o.Label))
            .ToList();
        string other = (_other.Text ?? "").Trim();
        if (picked.Count == 0 && other.Length == 0)
        {
            return null;
        }
        return PyDict.Of("picked", picked, "other", other);
    }

    public void Disable() => Root.IsEnabled = false;
}

/// <summary>结构化选择题卡(照 Windows 版 AskCard)。</summary>
internal sealed class AskCard : Border
{
    private readonly List<AskBlock> _blocks = new();
    private readonly StackPanel _body = UiClass.V(10);
    private readonly Action<object?> _submit;
    private readonly Button _ok;
    private readonly Button _skip;
    private bool _frozen;

    public AskCard(string title, List<object> questions, Action<object?> submit)
    {
        _submit = submit;
        if (!string.IsNullOrWhiteSpace(title))
        {
            _body.Children.Add(UiClass.Txt(title, 13, true).Wrap());
        }
        foreach (object? raw in questions)
        {
            var block = new AskBlock(Py.D(raw));
            _blocks.Add(block);
            _body.Children.Add(block.Root);
        }

        _ok = U.Btn("确定", BtnKind.Primary, (_, _) => Submit());
        _skip = U.Btn("跳过", BtnKind.Normal, (_, _) => Skip());
        _body.Children.Add(UiClass.H(8, _ok, _skip));

        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(12, 10, 12, 11);
        BorderThickness = new Thickness(1);
        MaxWidth = 760;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _body;
        this[!Border.BackgroundProperty] = Res.Bind("B.AccentSoft");
        this[!Border.BorderBrushProperty] = Res.Bind("B.Accent");
    }

    private void Submit()
    {
        if (_frozen)
        {
            return;
        }
        var answers = new PyDict();
        foreach (AskBlock block in _blocks)
        {
            PyDict? picked = block.Collect();
            if (picked == null)
            {
                ToastHost.Show("还没选", block.Prompt, ToastKind.Warning);
                return;
            }
            answers[block.Id] = picked;
        }
        Freeze("已提交");
        _submit(PyDict.Of("answers", answers));
    }

    private void Skip()
    {
        if (_frozen)
        {
            return;
        }
        Freeze("已跳过");
        _submit(null);
    }

    private void Freeze(string note)
    {
        _frozen = true;
        _ok.IsEnabled = false;
        _skip.IsEnabled = false;
        foreach (AskBlock block in _blocks)
        {
            block.Disable();
        }
        _body.Children.Add(UiClass.Small(note));
    }
}
