﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Xceed.Wpf.AvalonDock.Layout;
using System.IO;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Utils;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using System.Timers;

namespace Spedit.UI.Components
{
    /// <summary>
    /// Interaction logic for EditorElement.xaml
    /// </summary>
    public partial class EditorElement : UserControl
    {
        public new LayoutDocument Parent;

        FoldingManager foldingManager;
        SPFoldingStrategy foldingStrategy;
        ColorizeSelection colorizeSelection;
        Timer regularyTimer;
        bool WantFoldingUpdate = false;
        bool SelectionIsHighlited = false;

        public string FullFilePath
        {
            get { return _FullFilePath; }
            set
            {
                FileInfo fInfo = new FileInfo(value);
                _FullFilePath = fInfo.FullName;
                Parent.Title = fInfo.Name;
            }
        }
        private string _FullFilePath = "";

        public bool NeedsSave = false;

        public EditorElement()
        {
            InitializeComponent();
        }
        public EditorElement(string filePath)
        {
            InitializeComponent();

            editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
            editor.TextArea.SelectionChanged += TextArea_SelectionChanged;
            editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
            editor.PreviewMouseWheel += PrevMouseWheel;
            editor.MouseDown += editor_MouseDown;

            _FullFilePath = filePath;
            editor.Options.ConvertTabsToSpaces = false;
            editor.Options.EnableHyperlinks = false;
            editor.Options.HighlightCurrentLine = true;
            editor.TextArea.SelectionCornerRadius = 0.0;

            editor.FontFamily = new FontFamily(Program.OptionsObject.Editor_FontFamily);
            editor.WordWrap = Program.OptionsObject.Editor_WordWrap;
            UpdateFontSize(Program.OptionsObject.Editor_FontSize);

            colorizeSelection = new ColorizeSelection();

            editor.TextArea.TextView.LineTransformers.Add(colorizeSelection);
            editor.SyntaxHighlighting = new AeonEditorHighlighting();

            LoadAutoCompletes();

            editor.Load(filePath);
            NeedsSave = false;

            foldingManager = FoldingManager.Install(editor.TextArea);
            foldingStrategy = new SPFoldingStrategy();
            foldingStrategy.UpdateFoldings(foldingManager, editor.Document);

            regularyTimer = new Timer(2000.0);
            regularyTimer.Elapsed += regularyTimer_Elapsed;
            regularyTimer.Start();

            CompileBox.IsChecked = (bool?)filePath.EndsWith(".sp");
        }

        private void regularyTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
                    {
                        if (editor.SelectionLength > 0 && editor.SelectionLength < 50)
                        {
                            string selectionString = editor.SelectedText;
                            if (IsValidSearchSelectionString(selectionString))
                            {
                                colorizeSelection.SelectionString = selectionString;
                                colorizeSelection.HighlightSelection = true;
                                SelectionIsHighlited = true;
                                editor.TextArea.TextView.Redraw();
                            }
                            else
                            {
                                colorizeSelection.HighlightSelection = false;
                                colorizeSelection.SelectionString = string.Empty;
                                if (SelectionIsHighlited)
                                {
                                    editor.TextArea.TextView.Redraw();
                                    SelectionIsHighlited = false;
                                }
                            }
                        }
                        else
                        {
                            colorizeSelection.HighlightSelection = false;
                            colorizeSelection.SelectionString = string.Empty;
                            if (SelectionIsHighlited)
                            {
                                editor.TextArea.TextView.Redraw();
                                SelectionIsHighlited = false;
                            }
                        }
                    });
            if (WantFoldingUpdate)
            {
                WantFoldingUpdate = false;
                try //this "solves" a racing-conditions error - i wasnt able to fix it till today.. 
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        foldingStrategy.UpdateFoldings(foldingManager, editor.Document);
                    });
                }
                catch (Exception) { }
            }
        }

        public void Save(bool Force = false)
        {
            if (NeedsSave || Force)
            {
                editor.Save(_FullFilePath);
                NeedsSave = false;
            }
        }

        public void UpdateFontSize(double size)
        {
            editor.FontSize = size;
            StatusLine_FontSize.Text = size.ToString("n0") + " pt";
        }

        public async void Close(bool ForcedToSave = false, bool CheckSavings = true)
        {
            regularyTimer.Stop();
            regularyTimer.Close();
            if (CheckSavings)
            {
                if (NeedsSave)
                {
                    if (ForcedToSave)
                    {
                        Save();
                    }
                    else
                    {
                        var Result = await Program.MainWindow.ShowMessageAsync("Saving File '" + Parent.Title + "'", "", MessageDialogStyle.AffirmativeAndNegative, Program.MainWindow.MetroDialogOptions);
                        if (Result == MessageDialogResult.Affirmative)
                        {
                            Save();
                        }
                    }
                }
            }
            Program.MainWindow.EditorsReferences.Remove(this);
            //if (Parent.IsFloating) why was this here?...
            //{
            Parent.Close();
            //}
            Program.MainWindow.DockingPane.Children.Remove(Parent);
            Parent = null; //to prevent a ring depency which disables the GC from work

        }

        private void editor_TextChanged(object sender, EventArgs e)
        {
            WantFoldingUpdate = true;
            NeedsSave = true;
        }

        private void Caret_PositionChanged(object sender, EventArgs e)
        {
            StatusLine_Coloumn.Text = "Col " + editor.TextArea.Caret.Column.ToString();
            StatusLine_Line.Text = "Ln " + editor.TextArea.Caret.Line.ToString();
            EvaluateIntelliSense();
        }

        private void TextArea_SelectionChanged(object sender, EventArgs e)
        {
            StatusLine_SelectionLength.Text = "Len " + editor.SelectionLength.ToString();
        }

        private void PrevMouseWheel(object sender, MouseWheelEventArgs e)
        {
            editor.ScrollToVerticalOffset(editor.VerticalOffset + ((double)e.Delta * editor.FontSize * -0.01));
            e.Handled = true;
            HideISAC();
        }

        private void editor_MouseDown(object sender, MouseButtonEventArgs e)
        {
            HideISAC();
        }

        private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = ISAC_EvaluateKeyDownEvent(e.Key);
        }

        private void HandleContextMenuCommand(object sender, RoutedEventArgs e)
        {
            switch ((string)((MenuItem)sender).Tag)
            {
                case "0": { editor.Undo(); break; }
                case "1": { editor.Redo(); break; }
                case "2": { editor.Cut(); break; }
                case "3": { editor.Copy(); break; }
                case "4": { editor.Paste(); break; }
                case "5": { editor.SelectAll(); break; }
            }
        }

        private void ContextMenu_Opening(object sender, RoutedEventArgs e)
        {
            ((MenuItem)((ContextMenu)sender).Items[0]).IsEnabled = editor.CanUndo;
            ((MenuItem)((ContextMenu)sender).Items[1]).IsEnabled = editor.CanRedo;
        }

        private bool IsValidSearchSelectionString(string s)
        {
            int length = s.Length;
            for (int i = 0; i < length; ++i)
            {
                if (!((s[i] >= 'a' && s[i] <= 'z') || (s[i] >= 'A' && s[i] <= 'Z') || (s[i] >= '0' && s[i] <= '9') || (s[i] == '_')))
                {
                    return false;
                }
            }
            return true;
        }
    }

    public class ColorizeSelection : DocumentColorizingTransformer
    {
        public string SelectionString = string.Empty;
        public bool HighlightSelection = false;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (HighlightSelection)
            {
                if (string.IsNullOrWhiteSpace(SelectionString))
                {
                    return;
                }
                int lineStartOffset = line.Offset;
                string text = CurrentContext.Document.GetText(line);
                int start = 0;
                int index;
                while ((index = text.IndexOf(SelectionString, start)) >= 0)
                {
                    base.ChangeLinePart(
                        lineStartOffset + index,
                        lineStartOffset + index + SelectionString.Length,
                        (VisualLineElement element) =>
                        {
                            element.BackgroundBrush = Brushes.LightGray;
                        });
                    start = index + 1;
                }
            }
        }
    }

}
