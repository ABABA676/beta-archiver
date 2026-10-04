using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BetaApp;

/// <summary>Главное окно «Беты». Интерфейс собран кодом — визуальный конструктор не нужен.</summary>
public sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(238, 241, 246);
    private static readonly Color Card = Color.White;
    private static readonly Color Accent = Color.FromArgb(79, 98, 254);
    private static readonly Color Ink = Color.FromArgb(24, 28, 41);
    private static readonly Color Muted = Color.FromArgb(118, 125, 143);

    private readonly Label _dropTitle = new()
    {
        Dock = DockStyle.Fill,
        Text = "Перетащи сюда файлы или папку",
        ForeColor = Ink,
        Font = new Font("Segoe UI", 15f),
        TextAlign = ContentAlignment.BottomCenter
    };
    private readonly Label _dropHint = new()
    {
        Dock = DockStyle.Fill,
        Text = "файлы и папки → упакуем в .b3ta,   архив .b3ta → распакуем\n\n"
             + ".b3ta — это обычный 7z: его открывает и 7-Zip, и WinRAR, и любой другой распаковщик",
        ForeColor = Muted,
        TextAlign = ContentAlignment.TopCenter
    };
    private readonly ComboBox _level = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _openAfter = new()
    {
        Text = "После распаковки открыть папку",
        AutoSize = true,
        Checked = true,
        ForeColor = Ink
    };
    private readonly Button _pack = MakeButton("Упаковать в .b3ta", accent: true, width: 200);
    private readonly Button _extract = MakeButton("Распаковать", width: 150);
    private readonly Button _inside = MakeButton("Что внутри", width: 140);
    private readonly Button _cancel = MakeButton("Отмена", width: 110);
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Bottom, Style = ProgressBarStyle.Continuous, Height = 12 };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        Text = "Готов к работе",
        ForeColor = Muted,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Card,
        Font = new Font("Consolas", 9f),
        ScrollBars = ScrollBars.Vertical
    };
    private readonly Label _assocState = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 8.5f)
    };

    private string[] _lastDrop = [];
    private CancellationTokenSource? _cts;

    public MainForm(string[] args)
    {
        Text = "Бета — архиватор .b3ta";
        MinimumSize = new Size(660, 580);
        ClientSize = new Size(840, 660);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg;
        Font = new Font("Segoe UI", 9f);
        AllowDrop = true;

        for (var i = 0; i <= 9; i++)
            _level.Items.Add(i switch
            {
                0 => "0 — без сжатия (быстро)",
                1 or 2 or 3 => $"{i} — быстро",
                4 or 5 or 6 => $"{i} — нормально",
                _ => $"{i} — максимум (медленно, но меньше)"
            });
        _level.SelectedIndex = 5;

        Controls.Add(BuildDropCard());
        Controls.Add(BuildTools());
        Controls.Add(BuildLog());
        Controls.Add(BuildStatus());
        Controls.Add(BuildTop());
        Controls.Add(BuildMenu());

        _pack.Click += async (_, _) => await PackAsync(_lastDrop.Where(p => !Engine.LooksLikeArchive(p)).ToArray());
        _extract.Click += async (_, _) => await ExtractAsync(_lastDrop.FirstOrDefault(Engine.LooksLikeArchive) ?? "");
        _inside.Click += async (_, _) => await InsideAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        FormClosing += (_, e) => { if (_cts is not null) { _cts.Cancel(); e.Cancel = true; } };

        UpdateButtons();
        Shown += (_, _) =>
        {
            RefreshAssocState();
            Log($"Бета {AppInfo.Version} запущена. Движок: {Engine.ExePath}");
            if (!Engine.Available) Log("ВНИМАНИЕ: 7z.exe не найден — установи 7-Zip.");
            var file = args.FirstOrDefault(a => File.Exists(a));
            if (file is null) return;
            _lastDrop = [file];
            UpdateButtons();
            if (Engine.LooksLikeArchive(file)) _ = ExtractAsync(file);
            else _ = PackAsync(_lastDrop);
        };
    }

    // ---------- интерфейс ----------

    private static Button MakeButton(string text, bool accent = false, int width = 140)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(width, 40),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = accent ? Color.White : Ink,
            BackColor = accent ? Accent : Card,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 0),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = accent ? Accent : Color.FromArgb(210, 214, 224);
        return b;
    }

    private Control BuildDropCard()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Card,
            Padding = new Padding(22)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 40f));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
        card.Controls.Add(_dropTitle, 0, 1);
        card.Controls.Add(_dropHint, 0, 2);
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(198, 204, 220), 2) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, Rectangle.Inflate(card.ClientRectangle, -14, -14));
        };
        return card;
    }

    private Control BuildTop()
    {
        var title = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
        title.Controls.Add(new Label
        {
            Text = "БЕ́ТА",
            Dock = DockStyle.Fill,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        }, 0, 0);
        title.Controls.Add(new Label
        {
            Text = "архиватор с расширением .b3ta — совместим с 7-Zip и WinRAR",
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = ContentAlignment.TopLeft
        }, 0, 1);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 86,
            ColumnCount = 2,
            BackColor = Bg,
            Padding = new Padding(18, 8, 18, 6)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        top.Controls.Add(title, 0, 0);
        top.Controls.Add(_assocState, 1, 0);
        return top;
    }

    private Control BuildTools()
    {
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty
        };
        buttons.Controls.AddRange([_pack, _extract, _inside, _cancel]);

        var levelLabel = new Label
        {
            Text = "Сжатие:",
            AutoSize = true,
            ForeColor = Muted,
            Margin = new Padding(2, 10, 8, 0)
        };
        _level.Margin = new Padding(0, 4, 20, 0);
        _openAfter.Margin = new Padding(0, 8, 0, 0);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = Padding.Empty
        };
        options.Controls.AddRange([levelLabel, _level, _openAfter]);

        var tools = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 104,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Bg,
            Padding = new Padding(18, 6, 18, 6)
        };
        tools.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tools.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tools.Controls.Add(buttons, 0, 0);
        tools.Controls.Add(options, 0, 1);
        return tools;
    }

    private Control BuildLog()
    {
        var box = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 200,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Card,
            Padding = new Padding(18, 8, 18, 12)
        };
        box.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        box.Controls.Add(new Label
        {
            Text = "ЖУРНАЛ",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8f)
        }, 0, 0);
        box.Controls.Add(_log, 0, 1);
        return box;
    }

    private Control BuildStatus()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Bg,
            Padding = new Padding(18, 0, 18, 12)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(_status, 0, 0);
        _bar.Margin = new Padding(0, 6, 0, 0);
        panel.Controls.Add(_bar, 0, 1);
        return panel;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Bg };

        var archive = new ToolStripMenuItem("Архив");
        archive.DropDownItems.Add("Привязать расширение .b3ta…", null, (_, _) => Register());
        archive.DropDownItems.Add("Убрать привязку .b3ta", null, (_, _) => Unregister());
        archive.DropDownItems.Add(new ToolStripSeparator());
        archive.DropDownItems.Add("Выход", null, (_, _) => Close());

        var view = new ToolStripMenuItem("Вид");
        view.DropDownItems.Add("Очистить журнал", null, (_, _) => _log.Clear());

        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add("Почему .b3ta открывается везде…", null, (_, _) => AboutExtension());
        help.DropDownItems.Add("О программе", null, (_, _) => About());

        menu.Items.AddRange([archive, view, help]);
        MainMenuStrip = menu;
        return menu;
    }

    // ---------- действия ----------

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var ok = e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
        _lastDrop = paths;
        UpdateButtons();

        var archives = paths.Where(Engine.LooksLikeArchive).ToArray();
        var plain = paths.Where(p => !Engine.LooksLikeArchive(p)).ToArray();

        Log($"Принято: {paths.Length} объект(ов) — {string.Join(", ", paths.Select(Path.GetFileName))}");
        if (archives.Length > 0 && plain.Length == 0) await ExtractAsync(archives[0]);
        else if (plain.Length > 0) await PackAsync(plain);
    }

    private async Task PackAsync(string[] items)
    {
        if (items.Length == 0) return;
        if (!EnsureEngine()) return;

        var first = items[0].TrimEnd('\\', '/');
        var parent = Directory.Exists(first) ? Directory.GetParent(first)!.FullName : Path.GetDirectoryName(Path.GetFullPath(first))!;
        var stem = items.Length == 1 ? Path.GetFileNameWithoutExtension(first) : "архив";
        var dst = UniqueFile(Path.Combine(parent, stem + Engine.Extension));

        Log($"Упаковываю в {dst} (уровень {_level.SelectedIndex}, {items.Length} шт.)");
        await RunJobAsync($"Упаковка: {Path.GetFileName(dst)}", (ct, prog, log) =>
        {
            var run = Engine.Pack(dst, items, _level.SelectedIndex, prog, log, ct);
            log($"Готово: {dst} — {EntryInfo.Human(new FileInfo(dst).Length)}");
            return run;
        });
    }

    private async Task ExtractAsync(string archive)
    {
        if (string.IsNullOrEmpty(archive)) return;
        if (!EnsureEngine()) return;

        var parent = Path.GetDirectoryName(Path.GetFullPath(archive))!;
        var stem = Path.GetFileNameWithoutExtension(archive);
        var dst = UniqueDir(Path.Combine(parent, stem));

        Log($"Распаковываю {archive} → {dst}");
        await RunJobAsync($"Распаковка: {Path.GetFileName(archive)}", (ct, prog, log) =>
        {
            var run = Engine.Extract(archive, dst, prog, log, ct);
            log($"Готово: папка {dst}");
            if (_openAfter.Checked) OpenFolder(dst);
            return run;
        });
    }

    private async Task InsideAsync()
    {
        var archive = _lastDrop.FirstOrDefault(Engine.LooksLikeArchive);
        if (string.IsNullOrEmpty(archive)) return;
        await RunJobAsync($"Читаю список: {Path.GetFileName(archive)}", (_, _, log) =>
        {
            var list = Engine.List(archive);
            log($"Записей: {list.Count}");
            foreach (var e in list.Take(400)) log($"  {EntryInfo.Human(e.Size),10}  {e.Path}");
            if (list.Count > 400) log($"  …и ещё {list.Count - 400}");
            return new EngineRun { ExitCode = 0 };
        });
    }

    private async Task RunJobAsync(
        string title,
        Func<CancellationToken, Action<int>, Action<string>, EngineRun> job)
    {
        if (_cts is not null)
        {
            Warn("Уже выполняется операция — дождись её окончания или нажми «Отмена».");
            return;
        }
        if (!EnsureEngine()) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        _bar.Value = 0;
        SetStatus(title);

        try
        {
            var run = await Task.Run(() => job(
                ct,
                p => Ui(() => _bar.Value = Math.Clamp(p, _bar.Minimum, _bar.Maximum)),
                Log));

            _bar.Value = _bar.Maximum;
            if (run.ExitCode == 1) Log("7-Zip закончил с предупреждениями (код 1) — посмотри журнал.");
            SetStatus("Готово");
        }
        catch (OperationCanceledException)
        {
            Log("Операция отменена.");
            SetStatus("Отменено");
        }
        catch (Exception ex)
        {
            Log("ОШИБКА: " + ex.Message);
            SetStatus("Ошибка — подробности в журнале");
            Warn(ex.Message);
        }
        finally
        {
            _cts = null;
            SetBusy(false);
        }
    }

    // ---------- мелочи ----------

    private bool EnsureEngine()
    {
        if (Engine.Available) return true;
        Warn("Не найден 7z.exe — это движок архиватора.\n\nУстанови 7-Zip (https://www.7-zip.org) — и «Бета» заработает сразу.");
        return false;
    }

    private void UpdateButtons()
    {
        _pack.Enabled = _cts is null && _lastDrop.Any(p => !Engine.LooksLikeArchive(p));
        _extract.Enabled = _cts is null && _lastDrop.Any(Engine.LooksLikeArchive);
        _inside.Enabled = _cts is null && _lastDrop.Any(Engine.LooksLikeArchive);
    }

    private void SetBusy(bool busy)
    {
        Ui(() =>
        {
            _cancel.Enabled = busy;
            _pack.Enabled = !busy && _lastDrop.Any(p => !Engine.LooksLikeArchive(p));
            _extract.Enabled = !busy && _lastDrop.Any(Engine.LooksLikeArchive);
            _inside.Enabled = !busy && _lastDrop.Any(Engine.LooksLikeArchive);
        });
    }

    private void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            Warn("Не нашёл свой .exe — сначала собери программу (build.ps1).");
            return;
        }
        try
        {
            Association.Register(exe);
            RefreshAssocState();
            Log($"Привязал .b3ta к «{Path.GetFileName(exe)}». 7-Zip и WinRAR остались в «Открыть с помощью».");
            Info("Готово: .b3ta открывается двойным кликом в «Бете».\n\n"
               + "В меню «Открыть с помощью» есть 7-Zip и WinRAR — файл у них открывается как обычный 7z.\n"
               + "Привязка живёт только в твоём профиле Windows, у других людей ничего не меняется.");
        }
        catch (Exception ex) { Warn("Не получилось привязать: " + ex.Message); }
    }

    private void Unregister()
    {
        try
        {
            Association.Unregister();
            RefreshAssocState();
            Log("Привязка .b3ta убрана.");
        }
        catch (Exception ex) { Warn("Не получилось убрать привязку: " + ex.Message); }
    }

    private void RefreshAssocState() => Ui(() =>
        _assocState.Text = Association.IsRegistered()
            ? ".b3ta открывается здесь\nдвойной клик → «Бета»"
            : $".b3ta пока не привязан (сейчас: {Association.DefaultHint()})\nАрхив → Привязать расширение");

    private void About() => Info(
        $"Бета {AppInfo.Version}\n\n"
        + "Своё расширение .b3ta, внутри — обычный архив 7z.\n"
        + "Поэтому его открывают 7-Zip, WinRAR и любой другой распаковщик,\n"
        + "даже если «Беты» у человека никогда не было.\n\n"
        + $"Движок: {Engine.ExePath}\n"
        + "Стек: C# / .NET 8 / WinForms. Ноль сторонних библиотек.");

    private void AboutExtension() => Info(
        "Почему .b3ta открывается в 7-Zip и WinRAR\n\n"
        + "Программы определяют тип файла по первым байтам (сигнатуре), а не по расширению.\n"
        + "Внутри нашего файла — обычный 7z, поэтому 7-Zip и WinRAR понимают его как 7z.\n\n"
        + "Если нужно открыть на чужом компьютере:\n"
        + "  • «Открыть с помощью» → 7-Zip / WinRAR\n"
        + "  • перетащить файл на окно 7-Zip или на иконку WinRAR\n"
        + "  • просто переименовать .b3ta → .7z\n\n"
        + "Чего .b3ta не умеет: пункты «Извлечь сюда» в контекстном меню\n"
        + "для незнакомого расширения не появляются — это плата за своё расширение.");

    private static void OpenFolder(string dir)
    {
        try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
        catch { /* пользователь мог закрыть папку — не страшно */ }
    }

    private static string UniqueFile(string desired)
    {
        if (!File.Exists(desired)) return desired;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(
                Path.GetDirectoryName(desired)!,
                $"{Path.GetFileNameWithoutExtension(desired)} ({i}){Path.GetExtension(desired)}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(
            Path.GetDirectoryName(desired)!,
            $"{Path.GetFileNameWithoutExtension(desired)} ({Guid.NewGuid():N}){Path.GetExtension(desired)}");
    }

    private static string UniqueDir(string desired)
    {
        if (!Directory.Exists(desired) && !File.Exists(desired)) return desired;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{desired} ({i})";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        return $"{desired} ({Guid.NewGuid():N})";
    }

    private void SetStatus(string text) => Ui(() => _status.Text = text);

    private void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(line)); return; }
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
    }

    private void Ui(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch { } return; }
        action();
    }

    private void Info(string text) => MessageBox.Show(this, text, "Бета", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void Warn(string text) => MessageBox.Show(this, text, "Бета", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
