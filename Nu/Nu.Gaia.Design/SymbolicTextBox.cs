﻿using System;
using System.Drawing;
using System.Linq;
using ScintillaNET;
using ScintillaNET_FindReplaceDialog;
using System.Windows.Forms;

namespace Nu.Gaia.Design
{
#if WINDOWS
    public class SymbolicTextBox : Scintilla
    {
        public SymbolicTextBox()
        {
            // Make default styles monospaced!
            Styles[Style.Default].Font = "Lucida Console";

            // Add a little more line spacing for new font
            ExtraDescent = 1;

            // Lisp lexer
            Lexer = Lexer.Lisp;

            // Add comment styles.
            Styles[Style.Lisp.Comment].ForeColor = Color.ForestGreen;
            Styles[Style.Lisp.MultiComment].ForeColor = Color.ForestGreen;

            // Add keyword styles (keywords 0 are reserved for DSL-specific use)
            Styles[Style.Lisp.Keyword].ForeColor = Color.DarkBlue;
            Styles[Style.Lisp.KeywordKw].ForeColor = Color.FromArgb(0xFF, 0x60, 0x00, 0x70);
            SetKeywords(1, keywordsImplicit);

            // Add operator styles (braces, actually)
            Styles[Style.Lisp.Operator].ForeColor = Color.RoyalBlue;
            Styles[Style.BraceLight].BackColor = Color.LightBlue;
            Styles[Style.BraceBad].BackColor = Color.Red;

            // Add symbol styles (operators, actually)
            Styles[Style.Lisp.Special].ForeColor = Color.DarkBlue;

            // Add string style
            Styles[Style.Lisp.String].ForeColor = Color.Teal;

            // No tabs
            UseTabs = false;

            // Implement brace matching
            UpdateUI += SymbolicTextBox_UpdateUI;

            // Implement auto-complete
            CharAdded += SymbolicTextBox_CharAdded;

            // Implement find/replace
            MyFindReplace = new FindReplace(this);
            KeyDown += SymbolicTextBox_KeyDown;
        }

        public string Keywords0
        {
            get { return keywords0; }
            set
            {
                keywords0 = value;
                SetKeywords(0, keywords0);
            }
        }

        public string Keywords1
        {
            get { return keywords1; }
            set
            {
                keywords1 = value;
                SetKeywords(1, keywords1 + " " + keywordsImplicit);
            }
        }

        public string KeywordsImplicit
        {
            get { return keywordsImplicit; }
            set
            {
                keywordsImplicit = value;
                SetKeywords(1, keywords1 + " " + keywordsImplicit);
            }
        }

        public string AutoCWords
        {
            get
            {
                var keywordsSplit = keywords0.Split(' ').Distinct().ToArray();
                Array.Sort(keywordsSplit, StringComparer.Ordinal);
                var keywordsSorted = string.Join(AutoCSeparator.ToString(), keywordsSplit);
                return keywordsSorted;
            }
        }

        private void SymbolicTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && !e.Shift && e.KeyCode == Keys.F)
            {
                MyFindReplace.ShowIncrementalSearch();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && !e.Shift && e.KeyCode == Keys.H)
            {
                MyFindReplace.ShowReplace();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.F)
            {
                MyFindReplace.ShowFind();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.Shift && e.KeyCode == Keys.H)
            {
                MyFindReplace.ShowReplace();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.G)
            {
                GoTo MyGoTo = new GoTo(this);
                MyGoTo.ShowGoToDialog();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.F3)
            {
                // TODO: figure out how to call this from here...
                // https://github.com/Stumpii/ScintillaNET-FindReplaceDialog/issues
                // MyFindReplace.FindNext();
                // e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Escape)
            {
                MyFindReplace.ClearAllHighlights();
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Space)
            {
                AutoCShow(false);
                e.SuppressKeyPress = true;
            }
            else if (e.Control)
            {
                e.SuppressKeyPress = true;
                e.Handled = false;
            }
            else if (e.Alt && e.KeyCode == Keys.Up)
            {
                // TODO: SelectParentSymbols();
                e.SuppressKeyPress = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Down)
            {
                // TODO: SelectChildSymbols();
                e.SuppressKeyPress = true;
            }
            else if (e.Alt)
            {
                e.SuppressKeyPress = true;
                e.Handled = false;
            }
            // NOTE: auto [] completion feature disabled due to poor usability.
            //else if (e.KeyCode == Keys.OemOpenBrackets)
            //{
            //    // first, delete any selected text
            //    if (SelectionStart != SelectionEnd)
            //        DeleteRange(SelectionStart, SelectionEnd - SelectionStart);
            //
            //    // next, insert open _and_ close brackets
            //    InsertText(SelectionStart, "[]");
            //    ++SelectionStart;
            //    e.SuppressKeyPress = true;
            //}
        }

        private void SymbolicTextBox_CharAdded(object sender, CharAddedEventArgs e)
        {
            // NOTE: autocomplete is disabled when there are too many keywords due to a performance
            // bug in Scintilla IDE.
            if (AutoCWords.Split(' ').Length <= 32) AutoCShow(true);
        }

        private void SymbolicTextBox_UpdateUI(object sender, UpdateUIEventArgs e)
        {
            // Has the selection changed position?
            var selectionPos = SelectionStart;
            if (lastSelectionPos != selectionPos)
            {
                lastSelectionPos = selectionPos;
                var bracePos1 = -1;
                var bracePos2 = -1;
                if (selectionPos > 0 && IsBraceRight(GetCharAt(selectionPos - 1)))
                {
                    // Select the brace to the immediate left
                    bracePos1 = selectionPos - 1;
                }
                else if (IsBraceLeft(GetCharAt(selectionPos)))
                {
                    // Select the brace to the immediate right
                    bracePos1 = selectionPos;
                }

                if (bracePos1 >= 0)
                {
                    // Find the matching brace
                    bracePos2 = BraceMatch(bracePos1);
                    if (bracePos2 == InvalidPosition)
                    {
                        BraceBadLight(bracePos1);
                        HighlightGuide = 0;
                    }
                    else
                    {
                        BraceHighlight(bracePos1, bracePos2);
                        HighlightGuide = GetColumn(bracePos1);
                    }
                }
                else
                {
                    // Turn off brace matching
                    BraceHighlight(InvalidPosition, InvalidPosition);
                    HighlightGuide = 0;
                }
            }
        }

        private void AutoCShow(bool requireTextInCurrentWord)
        {
            // Find the word start
            var currentPos = CurrentPosition;
            var wordStartPos = WordStartPosition(currentPos, true);

            // Display the autocompletion list
            var lenEntered = currentPos - wordStartPos;
            if (!requireTextInCurrentWord || lenEntered > 0) AutoCShow(lenEntered, AutoCWords);
        }

        private bool IsBraceLeft(int c)
        {
            return c == '[';
        }

        private bool IsBraceRight(int c)
        {
            return c == ']';
        }

        private string keywords0 = string.Empty;
        private string keywords1 = string.Empty;
        private string keywordsImplicit = "True False Some None Right Left";
        private int lastSelectionPos = 0;
        private FindReplace MyFindReplace;
    }
#else
    public class SymbolicTextBox : TextBox
    {
        public SymbolicTextBox()
        {
            Multiline = true;
        }

        public string Keywords0 = "";
        public string Keywords1 = "";
        public string KeywordsImplicit = "";
        public string AutoCWords = "";
        public int SelectionEnd = 0;
        public int ExtraDescent = 0;
        public void EmptyUndoBuffer() { }
        public void ScrollCaret() { }
        public void EndUndoAction() { }
        public void GotoPosition(int position) { }
    }
#endif
}
