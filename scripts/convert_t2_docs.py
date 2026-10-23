from pathlib import Path
import re

from reportlab.lib import colors
from reportlab.lib.enums import TA_CENTER
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (
    Paragraph,
    SimpleDocTemplate,
    Spacer,
    Table,
    TableStyle,
    Preformatted,
    PageBreak,
)


ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'
OUTPUT = DOCS / 'pdf'
SOURCE_FILES = sorted(DOCS.glob('[0-9][0-9]_*.md'))


def register_font():
    candidates = [
        Path('C:/Windows/Fonts/arial.ttf'),
        Path('C:/Windows/Fonts/segoeui.ttf'),
    ]
    for candidate in candidates:
        if candidate.exists():
            pdfmetrics.registerFont(TTFont('PetmorFont', str(candidate)))
            return 'PetmorFont'
    return 'Helvetica'


def clean_inline(value):
    value = re.sub(r'!\[([^]]*)\]\([^)]*\)', r'[Imagen: \1]', value)
    value = re.sub(r'\[([^]]+)\]\([^)]*\)', r'\1', value)
    value = value.replace('**', '').replace('`', '')
    return value.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def parse_markdown(path, font):
    styles = getSampleStyleSheet()
    styles.add(ParagraphStyle('PetmorTitle', parent=styles['Title'], fontName=font, fontSize=20, leading=25, alignment=TA_CENTER, textColor=colors.HexColor('#087f8c'), spaceAfter=14))
    styles.add(ParagraphStyle('PetmorH2', parent=styles['Heading2'], fontName=font, fontSize=14, leading=18, textColor=colors.HexColor('#087f8c'), spaceBefore=10, spaceAfter=6))
    styles.add(ParagraphStyle('PetmorH3', parent=styles['Heading3'], fontName=font, fontSize=11, leading=15, textColor=colors.HexColor('#444444'), spaceBefore=7, spaceAfter=4))
    styles.add(ParagraphStyle('PetmorBody', parent=styles['BodyText'], fontName=font, fontSize=9.5, leading=13, spaceAfter=5))
    styles.add(ParagraphStyle('PetmorBullet', parent=styles['BodyText'], fontName=font, fontSize=9.5, leading=13, leftIndent=14, firstLineIndent=-8, spaceAfter=3))
    styles.add(ParagraphStyle('PetmorCode', parent=styles['Code'], fontName='Courier', fontSize=7.2, leading=9, backColor=colors.HexColor('#f3f5f6'), borderColor=colors.HexColor('#d9e1e3'), borderWidth=0.5, borderPadding=6, spaceAfter=8))

    story = []
    lines = path.read_text(encoding='utf-8').splitlines()
    index = 0
    while index < len(lines):
        line = lines[index].rstrip()
        if not line:
            index += 1
            continue
        if line.startswith('```'):
            code = []
            index += 1
            while index < len(lines) and not lines[index].startswith('```'):
                code.append(lines[index])
                index += 1
            story.append(Preformatted('\n'.join(code), styles['PetmorCode']))
            index += 1
            continue
        if line.startswith('# '):
            story.append(Paragraph(clean_inline(line[2:]), styles['PetmorTitle']))
        elif line.startswith('## '):
            story.append(Paragraph(clean_inline(line[3:]), styles['PetmorH2']))
        elif line.startswith('### '):
            story.append(Paragraph(clean_inline(line[4:]), styles['PetmorH3']))
        elif line.startswith('|'):
            table_lines = []
            while index < len(lines) and lines[index].startswith('|'):
                if not re.match(r'^\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|?$', lines[index]):
                    cells = [clean_inline(cell.strip()) for cell in lines[index].strip('|').split('|')]
                    table_lines.append(cells)
                index += 1
            if table_lines:
                width = min(175 * mm / max(len(table_lines[0]), 1), 175 * mm)
                table = Table(table_lines, colWidths=[width] * len(table_lines[0]), repeatRows=1)
                table.setStyle(TableStyle([
                    ('FONTNAME', (0, 0), (-1, -1), font),
                    ('FONTSIZE', (0, 0), (-1, -1), 7.5),
                    ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#087f8c')),
                    ('TEXTCOLOR', (0, 0), (-1, 0), colors.white),
                    ('GRID', (0, 0), (-1, -1), 0.35, colors.HexColor('#b8c9cc')),
                    ('VALIGN', (0, 0), (-1, -1), 'TOP'),
                    ('LEFTPADDING', (0, 0), (-1, -1), 5),
                    ('RIGHTPADDING', (0, 0), (-1, -1), 5),
                ]))
                story.extend([table, Spacer(1, 5)])
            continue
        elif re.match(r'^[-*] ', line):
            story.append(Paragraph('• ' + clean_inline(line[2:]), styles['PetmorBullet']))
        elif re.match(r'^\d+\. ', line):
            story.append(Paragraph(clean_inline(line), styles['PetmorBullet']))
        else:
            story.append(Paragraph(clean_inline(line), styles['PetmorBody']))
        index += 1
    return story


def add_page_number(canvas, document):
    canvas.saveState()
    canvas.setFont('Helvetica', 8)
    canvas.setFillColor(colors.HexColor('#6b7280'))
    canvas.drawRightString(195 * mm, 10 * mm, f'Petmor T2 | Página {document.page}')
    canvas.restoreState()


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    font = register_font()
    for source in SOURCE_FILES:
        destination = OUTPUT / f'{source.stem}.pdf'
        document = SimpleDocTemplate(
            str(destination), pagesize=A4,
            rightMargin=18 * mm, leftMargin=18 * mm,
            topMargin=16 * mm, bottomMargin=16 * mm,
            title=source.stem,
            author='Petmor',
        )
        document.build(parse_markdown(source, font), onFirstPage=add_page_number, onLaterPages=add_page_number)
        print(destination.name)


if __name__ == '__main__':
    main()
