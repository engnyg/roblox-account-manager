"""
預設 QSS 主題。
對應 C# Forms/ThemeEditor.cs 的內建主題。
"""

DARK_THEME = """
QMainWindow, QDialog, QWidget {
    background-color: #1e1e2e;
    color: #cdd6f4;
    font-family: "Segoe UI", sans-serif;
    font-size: 10pt;
}

QMenuBar {
    background-color: #181825;
    color: #cdd6f4;
    border-bottom: 1px solid #313244;
}
QMenuBar::item:selected { background-color: #313244; }

QMenu {
    background-color: #1e1e2e;
    border: 1px solid #45475a;
    color: #cdd6f4;
}
QMenu::item:selected { background-color: #313244; }

QTableView, QTreeView, QListView {
    background-color: #181825;
    alternate-background-color: #1e1e2e;
    color: #cdd6f4;
    gridline-color: #313244;
    border: 1px solid #45475a;
    selection-background-color: #45475a;
    selection-color: #cdd6f4;
}
QHeaderView::section {
    background-color: #313244;
    color: #cdd6f4;
    border: none;
    padding: 4px;
}

QPushButton {
    background-color: #313244;
    color: #cdd6f4;
    border: 1px solid #45475a;
    border-radius: 4px;
    padding: 4px 12px;
    min-height: 22px;
}
QPushButton:hover { background-color: #45475a; }
QPushButton:pressed { background-color: #585b70; }
QPushButton:disabled { color: #6c7086; }

QLineEdit, QTextEdit, QPlainTextEdit {
    background-color: #181825;
    color: #cdd6f4;
    border: 1px solid #45475a;
    border-radius: 4px;
    padding: 3px;
}
QLineEdit:focus { border-color: #89b4fa; }

QComboBox {
    background-color: #313244;
    color: #cdd6f4;
    border: 1px solid #45475a;
    border-radius: 4px;
    padding: 3px 6px;
}
QComboBox::drop-down { border: none; }
QComboBox QAbstractItemView { background-color: #1e1e2e; color: #cdd6f4; }

QCheckBox { color: #cdd6f4; }
QCheckBox::indicator { width: 14px; height: 14px; border: 1px solid #45475a; border-radius: 2px; background: #181825; }
QCheckBox::indicator:checked { background: #89b4fa; }

QTabWidget::pane { border: 1px solid #45475a; }
QTabBar::tab {
    background: #313244;
    color: #cdd6f4;
    padding: 6px 16px;
    border: 1px solid #45475a;
}
QTabBar::tab:selected { background: #45475a; color: #89b4fa; }

QScrollBar:vertical {
    background: #181825;
    width: 10px;
    border: none;
}
QScrollBar::handle:vertical { background: #45475a; border-radius: 5px; min-height: 20px; }
QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }

QStatusBar { background-color: #181825; color: #a6adc8; }

QGroupBox {
    color: #89b4fa;
    border: 1px solid #45475a;
    border-radius: 4px;
    margin-top: 8px;
    padding-top: 8px;
}
QGroupBox::title { subcontrol-origin: margin; left: 8px; }

QSplitter::handle { background: #313244; }
QProgressBar {
    background-color: #181825;
    border: 1px solid #45475a;
    border-radius: 4px;
    text-align: center;
    color: #cdd6f4;
}
QProgressBar::chunk { background-color: #89b4fa; border-radius: 3px; }

QToolTip { background-color: #313244; color: #cdd6f4; border: 1px solid #45475a; }
"""

LIGHT_THEME = """
QMainWindow, QDialog, QWidget {
    background-color: #eff1f5;
    color: #4c4f69;
    font-family: "Segoe UI", sans-serif;
    font-size: 10pt;
}
QMenuBar { background-color: #dce0e8; color: #4c4f69; border-bottom: 1px solid #ccd0da; }
QMenuBar::item:selected { background-color: #ccd0da; }
QMenu { background-color: #eff1f5; border: 1px solid #ccd0da; color: #4c4f69; }
QMenu::item:selected { background-color: #ccd0da; }
QTableView, QTreeView, QListView {
    background-color: #dce0e8;
    alternate-background-color: #eff1f5;
    color: #4c4f69;
    gridline-color: #ccd0da;
    border: 1px solid #ccd0da;
    selection-background-color: #8caaee;
    selection-color: #eff1f5;
}
QHeaderView::section { background-color: #ccd0da; color: #4c4f69; border: none; padding: 4px; }
QPushButton {
    background-color: #dce0e8;
    color: #4c4f69;
    border: 1px solid #ccd0da;
    border-radius: 4px;
    padding: 4px 12px;
    min-height: 22px;
}
QPushButton:hover { background-color: #ccd0da; }
QPushButton:pressed { background-color: #acb0be; }
QLineEdit, QTextEdit, QPlainTextEdit {
    background-color: #dce0e8;
    color: #4c4f69;
    border: 1px solid #ccd0da;
    border-radius: 4px;
    padding: 3px;
}
"""

THEMES = {
    "Dark": DARK_THEME,
    "Light": LIGHT_THEME,
}
