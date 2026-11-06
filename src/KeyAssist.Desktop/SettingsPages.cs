using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeyAssist.Core;

namespace KeyAssist.Desktop;

public partial class MainWindow
{
    private void BuildCalibration()
    {
        var body = Card(PageContent, "遊戲內容區校準", "calibration");
        body.Children.Add(Text("手動填寫內容區尺寸和 DPI。以左上角 (0, 0) 為原點，往右為 X，往下為 Y。", Muted));
        var row = Wrap();
        var width = new TextBox { Text = Format(draft.Calibration.Width) };
        var height = new TextBox { Text = Format(draft.Calibration.Height) };
        var dpi = new TextBox { Text = Format(draft.Calibration.Dpi) };
        foreach (var input in new[] { width, height, dpi })
            input.TextChanged += (_, _) =>
            {
                draft.Calibration = new(Parse(width), Parse(height), Parse(dpi));
                Changed();
            };
        Labelled(row, "寬（像素）", width, 96);
        Labelled(row, "高（像素）", height, 96);
        Labelled(row, "DPI（100% 為 96）", dpi, 130);
        body.Children.Add(row);
        body.Children.Add(Register(Button("以目前校準確認已填座標", ConfirmPositions), "calibration.confirm"));
        static string Format(int value) => value > 0 ? value.ToString() : "";
        static int Parse(TextBox box) => int.TryParse(box.Text, out var value) && value > 0 ? value : 0;
    }

    private void ConfirmEditedPosition(string id, string x, string y)
    {
        if (PositionInBounds(x, y) &&
            (!draft.ConfirmedPositions.TryGetValue(id, out var previous) || previous == draft.Calibration))
            draft.ConfirmedPositions[id] = draft.Calibration;
    }

    private bool PositionInBounds(string x, string y) =>
        draft.Calibration.IsValid &&
        int.TryParse(x, out var left) && left >= 0 && left < draft.Calibration.Width &&
        int.TryParse(y, out var top) && top >= 0 && top < draft.Calibration.Height;

    private void ConfirmPositions()
    {
        if (!draft.Calibration.IsValid)
        {
            StatusText.Text = "請先填寫正整數的寬、高及 DPI。";
            return;
        }
        var count = 0;
        foreach (var field in FeatureRequirements.Fields.Values.Where(field => field.Kind == FieldKind.Position))
            Confirm(field.Id, draft.Get(field.Id + ".x"), draft.Get(field.Id + ".y"));
        foreach (var point in draft.Points)
            Confirm($"point.{point.Id}", point.X, point.Y);
        if (count > 0)
            Changed();
        StatusText.Text = $"已確認 {count} 個座標。請儲存設定。";

        void Confirm(string id, string x, string y)
        {
            if (!PositionInBounds(x, y))
                return;
            draft.ConfirmedPositions[id] = draft.Calibration;
            count++;
        }
    }

    private void BuildFlasks(Panel body)
    {
        body.Children.Add(Text("每槽使用獨立偵測點。開啟補喝時需要輸出鍵。", Muted));
        for (var i = 0; i < draft.Flasks.Count; i++)
        {
            var slotIndex = i;
            var slot = draft.Flasks[i];
            var id = $"F38.{i + 1}";
            var row = Card(body, $"藥劑 {i + 1}", id);
            var options = Wrap();
            options.Children.Add(Check("啟用本槽", slot.Enabled, enabled => Edit(f => f with { Enabled = enabled })));
            options.Children.Add(Check("補喝", slot.Refill, refill => Edit(f => f with { Refill = refill })));
            row.Children.Add(options);
            var fields = Wrap();
            Labelled(fields, "偵測點", PointSelect(slot.PointId?.ToString() ?? "", value =>
                Edit(f => f with { PointId = Guid.TryParse(value, out var point) ? point : null }), id + ".point"));
            Labelled(fields, "補喝輸出鍵", Hotkey(slot.Key, key => Edit(f => f with { Key = key }), id + ".key"));
            row.Children.Add(fields);

            void Edit(Func<FlaskSlot, FlaskSlot> change)
            {
                draft.Flasks[slotIndex] = change(draft.Flasks[slotIndex]);
                Changed();
            }
        }
    }

    private void BuildPoints(Panel body)
    {
        body.Children.Add(Text("手動輸入像素的 X、Y 和 #RRGGBB。正式快捷鍵擷取屬後續開發。", Muted));
        var entries = new StackPanel();
        body.Children.Add(Register(Button("＋ 新增偵測點", () =>
        {
            var point = new DetectionPoint(Guid.NewGuid(), $"偵測點 {draft.Points.Count + 1}");
            draft.Points.Add(point);
            Changed();
            Render();
            OpenSetting($"point.{point.Id}.name");
        }), "F22.add"));
        body.Children.Add(entries);
        Render();

        void Render()
        {
            foreach (var key in targets.Keys.Where(key => key.StartsWith("point.", StringComparison.Ordinal)).ToArray())
                targets.Remove(key);
            entries.Children.Clear();
            for (var i = 0; i < draft.Points.Count; i++)
            {
                var point = draft.Points[i];
                var position = i;
                var id = $"point.{point.Id}";
                var row = Card(entries, point.Name, id);
                var fields = Wrap();
                Labelled(fields, "點位名稱", Input(point.Name, value => Edit(p => p with { Name = value }), id + ".name"));
                Labelled(fields, "X", Input(point.X, value => Edit(p => p with { X = value }, true), id + ".x"), 65);
                Labelled(fields, "Y", Input(point.Y, value => Edit(p => p with { Y = value }, true), id + ".y"), 65);
                Labelled(fields, "RGB", Input(point.Color, value => Edit(p => p with { Color = value }), id + ".color"), 95);
                row.Children.Add(fields);
                var actions = Wrap();
                var up = Button("上移", () => Move(-1));
                up.IsEnabled = i > 0;
                actions.Children.Add(up);
                var down = Button("下移", () => Move(1));
                down.IsEnabled = i + 1 < draft.Points.Count;
                actions.Children.Add(down);
                actions.Children.Add(Button("刪除點位", () =>
                {
                    draft.Points.RemoveAt(position);
                    draft.ConfirmedPositions.Remove(id);
                    Changed();
                    Render();
                }, id + ".delete"));
                row.Children.Add(actions);

                void Edit(Func<DetectionPoint, DetectionPoint> change, bool coordinates = false)
                {
                    var edited = change(draft.Points[position]);
                    draft.Points[position] = edited;
                    if (coordinates)
                        ConfirmEditedPosition(id, edited.X, edited.Y);
                    Changed();
                }

                void Move(int offset)
                {
                    var other = position + offset;
                    (draft.Points[position], draft.Points[other]) = (draft.Points[other], draft.Points[position]);
                    Changed();
                    Render();
                    OpenSetting(id + ".name");
                }
            }
        }
    }

    private void BuildCycle(Panel body)
    {
        var rows = new StackPanel();
        body.Children.Add(Register(Button("＋ 新增循環步驟", () =>
        {
            draft.Cycle.Add(new());
            Changed();
            Render();
        }), "F11.add"));
        body.Children.Add(rows);
        Render();

        void Render()
        {
            foreach (var key in targets.Keys.Where(key => key.StartsWith("F11.step.", StringComparison.Ordinal)).ToArray())
                targets.Remove(key);
            rows.Children.Clear();
            for (var i = 0; i < draft.Cycle.Count; i++)
            {
                var position = i;
                var step = draft.Cycle[i];
                var row = Wrap();
                Labelled(row, $"步驟 {i + 1} 按鍵", Hotkey(step.Key, key => Edit(s => s with { Key = key }), $"F11.step.{i}"));
                Labelled(row, "延遲（毫秒）", Input(step.Delay, delay => Edit(s => s with { Delay = delay })), 110);
                row.Children.Add(Button("刪除步驟", () =>
                {
                    draft.Cycle.RemoveAt(position);
                    Changed();
                    Render();
                }));
                rows.Children.Add(row);

                void Edit(Func<SkillStep, SkillStep> change)
                {
                    draft.Cycle[position] = change(draft.Cycle[position]);
                    Changed();
                }
            }
        }
    }

    private void BuildInventory(Panel body)
    {
        body.Children.Add(Text("背包 12 × 5 鎖定格。選取格子可切換是否保留。", Muted));
        var cells = new System.Windows.Controls.Primitives.UniformGrid { Columns = 12 };
        body.Children.Add(cells);
        for (var i = 0; i < 60; i++)
        {
            var cell = i;
            var button = new Button { Content = (i + 1).ToString() };
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(1);
            button.MinHeight = 28;
            Update();
            button.Click += (_, _) =>
            {
                if (!draft.LockedCells.Add(cell))
                    draft.LockedCells.Remove(cell);
                Update();
                Changed();
            };
            cells.Children.Add(button);

            void Update()
            {
                var locked = draft.LockedCells.Contains(cell);
                button.Background = locked ? Paint("#BEE8D8") : Brushes.White;
                System.Windows.Automation.AutomationProperties.SetName(button, $"背包格 {cell + 1} · {(locked ? "已鎖定" : "未鎖定")}");
            }
        }
    }
}
