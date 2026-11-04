"""Read exported geometry and draw an engineering reference, not game artwork."""
import json, math, sys
from pathlib import Path
from html import escape
import xml.etree.ElementTree as ET

folder = Path(sys.argv[1])
track = json.loads((folder / 'mixed-tricks.track.json').read_text(encoding='utf-8'))
context = json.loads((folder / 'mixed-tricks.context.json').read_text(encoding='utf-8'))
bands = {item['id']: item['bands'] for item in context['physicalBands']}
W, H, left, right = 1500, 1130, 85, 35
sx, sy = (W-left-right)/1600, .48
parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">',
'<rect width="100%" height="100%" fill="#faf8ef"/>',
'<style>text{font-family:"Segoe UI",Arial,sans-serif;fill:#424a40} .small{font-size:12px}.title{font-size:24px;font-weight:600}.sub{font-size:15px}</style>',
'<text x="32" y="35" class="title">Полоса уловок · геометрия из JSON</text>',
'<text x="32" y="61" class="sub">Пять последовательных участков. X растёт вправо, Y — вниз. Высота 240; скорость без эффектов 220 ед/с.</text>',
'<text x="32" y="84" class="small">O — препятствия; R — зоны (номера соответствуют порядку в JSON). Зелёные — бонусы, розовые — ловушки, золотые — монеты.</text>',
'<text x="32" y="103" class="small">Это схема координат с разными масштабами X/Y, а не снимок игры и не доказательство проходимости.</text>']
for row in range(5):
    start = row * 1600
    stop = min(start+1600, track['length'])
    y0 = 160 + row*190
    def xy(x,y): return left+(x-start)*sx, y0+y*sy
    width = (stop-start)*sx
    parts += [f'<text x="{left}" y="{y0-24}" class="sub">X {start}–{stop} · базовое время {start/220:.2f}–{stop/220:.2f} с</text>',
              f'<rect x="{left}" y="{y0}" width="{width}" height="{240*sy}" fill="#f2f4e7" stroke="#a1ac94"/>',
              f'<defs><clipPath id="p{row}"><rect x="{left}" y="{y0}" width="{width}" height="{240*sy}"/></clipPath></defs>']
    for yy in (0,120,240):
        parts.append(f'<text x="{left-12}" y="{y0+yy*sy+4}" text-anchor="end" class="small">{yy}</text>')
    for tick in range(start, stop+1, 200):
        x,y=xy(tick,240)
        parts.append(f'<path d="M{x},{y0}V{y}" stroke="#d9dfce" stroke-dasharray="3 5"/>')
        parts.append(f'<text x="{x}" y="{y+19}" text-anchor="middle" class="small">{tick}</text>')
    parts.append(f'<g clip-path="url(#p{row})">')
    for i,o in enumerate(track['obstacles'],1):
        for b in bands[o['id']]:
            x,y=xy(b['x'],b['y'])
            color='#b4abc6' if o['kind']=='gate' else '#acb8a1'
            parts.append(f'<rect x="{x}" y="{y}" width="{b["width"]*sx}" height="{b["height"]*sy}" fill="{color}" stroke="#677360" stroke-width="1"/>')
        if start <= o['x'] < stop:
            x,y=xy(o['x'], 20 if o['kind']=='ceiling' else 224)
            parts.append(f'<text x="{x+4}" y="{y}" class="small">O{i}</text>')
    for i,z in enumerate(track['riskZones'],1):
        x,y=xy(z['x'],z['y'])
        color = '#a77984' if z['kind']=='negative' else '#ad8b37' if z['kind']=='coin' else '#548672'
        if z['kind']=='landing':
            a,b=xy(z['x']-z['landingWidth']/2,z['y']); c,d=xy(z['x']+z['landingWidth']/2,z['y'])
            parts.append(f'<path d="M{a},{b}H{c}" stroke="{color}" stroke-width="5"/>')
        elif z['kind']=='finish':
            for s in context['world']['finishTargetSegments']:
                x1,y1=xy(z['x']+s['x1'],z['y']+s['y1']); x2,y2=xy(z['x']+s['x2'],z['y']+s['y2'])
                parts.append(f'<path d="M{x1},{y1}L{x2},{y2}" stroke="{color}" stroke-width="2" fill="none"/>')
        else:
            r=z['radius']
            parts.append(f'<ellipse cx="{x}" cy="{y}" rx="{r*sx}" ry="{r*sy}" fill="none" stroke="{color}" stroke-width="2"/>')
        if start <= z['x'] < stop:
            parts.append(f'<text x="{x+9}" y="{y-7}" class="small" style="fill:{color}">R{i}</text>')
    parts.append('</g>')
parts.append('<text x="32" y="1120" class="small">Источник: TracksMixed.Get(), ObstacleGeometry.TryGetBand(), FinishTargetGeometry; экспорт 4 ноября 2026. Коллизии учитывают радиус героя 14.</text></svg>')
svg='\n'.join(parts)
ET.fromstring(svg)
target=folder/'mixed-tricks.overview.svg'
with target.open('x',encoding='utf-8',newline='\n') as f: f.write(svg)
print(target)
