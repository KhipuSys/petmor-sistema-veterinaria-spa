from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.platypus import (
    PageBreak,
    Paragraph,
    Preformatted,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
)


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Docs" / "INFORME_MANTENIMIENTO_PETMOR.md"
TARGET = ROOT / "Docs" / "PDF" / "INFORME_MANTENIMIENTO_PETMOR.pdf"


def clean(value):
    return escape(value.strip().replace("`", ""))


def is_separator(line):
    cells = line.strip().strip("|").split("|")
    return all(cell.strip().replace("-", "").replace(":", "") == "" for cell in cells)


def make_table(rows, styles):
    columns = len(rows[0])
    widths = {
        2: [82 * mm, 82 * mm],
        3: [40 * mm, 66 * mm, 58 * mm],
    }.get(columns, [164 * mm / columns] * columns)
    data = []
    for row_index, row in enumerate(rows):
        style = styles["TableHeader"] if row_index == 0 else styles["TableCell"]
        data.append([Paragraph(clean(cell), style) for cell in row])

    table = Table(data, colWidths=widths, repeatRows=1, hAlign="LEFT")
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#124e66")),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("GRID", (0, 0), (-1, -1), 0.45, colors.HexColor("#94a3b8")),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 6),
        ("RIGHTPADDING", (0, 0), (-1, -1), 6),
        ("TOPPADDING", (0, 0), (-1, -1), 5),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 5),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1), [colors.white, colors.HexColor("#f1f5f9")]),
    ]))
    return table


def footer(canvas, document):
    canvas.saveState()
    canvas.setFont("Helvetica", 7)
    canvas.setFillColor(colors.HexColor("#64748b"))
    canvas.drawCentredString(A4[0] / 2, 9 * mm, f"Petmor | Informe academico | Pagina {document.page}")
    canvas.restoreState()


def build():
    base = getSampleStyleSheet()
    styles = {
        "Title": ParagraphStyle("ReportTitle", parent=base["Title"], fontName="Helvetica-Bold", fontSize=17, leading=21, alignment=TA_CENTER, textColor="#124e66", spaceAfter=16),
        "H1": ParagraphStyle("ReportH1", parent=base["Heading1"], fontName="Helvetica-Bold", fontSize=14, leading=17, textColor="#124e66", spaceBefore=10, spaceAfter=7),
        "H2": ParagraphStyle("ReportH2", parent=base["Heading2"], fontName="Helvetica-Bold", fontSize=11, leading=14, textColor="#1d6b83", spaceBefore=8, spaceAfter=5),
        "Body": ParagraphStyle("ReportBody", parent=base["BodyText"], fontName="Helvetica", fontSize=9, leading=12, spaceAfter=4),
        "Bullet": ParagraphStyle("ReportBullet", parent=base["BodyText"], fontName="Helvetica", fontSize=9, leading=12, leftIndent=14, firstLineIndent=-8, spaceAfter=3),
        "Code": base["Code"],
        "TableHeader": ParagraphStyle("TableHeader", parent=base["BodyText"], fontName="Helvetica-Bold", fontSize=7.2, leading=9, textColor=colors.white),
        "TableCell": ParagraphStyle("TableCell", parent=base["BodyText"], fontName="Helvetica", fontSize=7.2, leading=9),
    }

    lines = SOURCE.read_text(encoding="utf-8").splitlines()
    story = []
    index = 0
    in_code = False
    code = []

    while index < len(lines):
        raw = lines[index]
        line = raw.strip("\ufeff")
        if line.startswith("```"):
            if in_code:
                story.append(Preformatted("\n".join(code), styles["Code"]))
                story.append(Spacer(1, 5))
                code = []
                in_code = False
            else:
                in_code = True
            index += 1
            continue
        if in_code:
            code.append(raw)
            index += 1
            continue
        if line.startswith("|") and index + 1 < len(lines) and is_separator(lines[index + 1]):
            rows = []
            while index < len(lines) and lines[index].strip().startswith("|"):
                if not (rows and is_separator(lines[index])):
                    rows.append([cell.strip() for cell in lines[index].strip().strip("|").split("|")])
                index += 1
            story.append(make_table(rows, styles))
            story.append(Spacer(1, 8))
            continue
        if not line.strip():
            story.append(Spacer(1, 3))
        else:
            plain = line.replace("`", "").replace("**", "")
            if plain.startswith("# "):
                story.append(Paragraph(clean(plain[2:]), styles["Title"]))
            elif plain.startswith("## "):
                story.append(Paragraph(clean(plain[3:]), styles["H1"]))
            elif plain.startswith("### "):
                story.append(Paragraph(clean(plain[4:]), styles["H2"]))
            elif plain.startswith("- "):
                story.append(Paragraph(clean(plain[2:]), styles["Bullet"], bulletText="•"))
            else:
                story.append(Paragraph(clean(plain), styles["Body"]))
        index += 1

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    document = SimpleDocTemplate(str(TARGET), pagesize=A4, rightMargin=16 * mm, leftMargin=16 * mm, topMargin=14 * mm, bottomMargin=16 * mm)
    document.build(story, onFirstPage=footer, onLaterPages=footer)


if __name__ == "__main__":
    build()
