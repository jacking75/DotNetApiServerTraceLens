"""
styles.css의 하드코딩 색을 의미 있는 CSS 변수로 바꾸고, 라이트 테마 값을 함께 만든다.

라이트 값은 색상(hue)과 채도를 유지한 채 명도만 뒤집어 만들고,
글자로 쓰이는 색은 밝은 배경 위에서 대비 4.5:1을 넘을 때까지 어둡게 내린다.
"손으로 21개만 재정의"한 예전 방식이 E2(안 보이는 글자)를 만든 원인이라,
변수와 라이트 값을 한 자리에서 기계적으로 만든다.
"""
import colorsys
import re
from collections import defaultdict

CSS = 'src/styles.css'

COLOR_PROPS = (
    'background', 'background-color', 'color', 'border', 'border-color',
    'border-top', 'border-bottom', 'border-left', 'border-right',
    'border-top-color', 'border-bottom-color', 'border-left-color', 'border-right-color',
    'box-shadow', 'fill', 'stroke', 'outline', 'outline-color', 'caret-color',
)
PROP_RE = re.compile(r'(' + '|'.join(sorted(COLOR_PROPS, key=len, reverse=True)) + r')\s*:\s*([^;}]+)')
HEX_RE = re.compile(r'#[0-9a-fA-F]{3,8}')


def parse_hex(value):
    """#rgb · #rrggbb · #rrggbbaa → (r, g, b, alpha 문자열)."""
    text = value[1:]
    if len(text) == 3:
        text = ''.join(char * 2 for char in text)
    alpha = ''
    if len(text) == 8:
        alpha = text[6:]
        text = text[:6]
    elif len(text) == 4:
        alpha = text[3] * 2
        text = text[:3]
    if len(text) != 6:
        return None
    return int(text[0:2], 16), int(text[2:4], 16), int(text[4:6], 16), alpha


def relative_luminance(rgb):
    def channel(value):
        value /= 255
        return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4
    r, g, b = (channel(component) for component in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(first, second):
    a, b = relative_luminance(first), relative_luminance(second)
    lighter, darker = max(a, b), min(a, b)
    return (lighter + 0.05) / (darker + 0.05)


def to_hex(rgb, alpha=''):
    return '#%02x%02x%02x%s' % (int(rgb[0]), int(rgb[1]), int(rgb[2]), alpha)


def hue_bucket(rgb):
    r, g, b = (component / 255 for component in rgb)
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    degrees = h * 360
    if s < 0.18:
        return 'neutral'
    if degrees < 18 or degrees >= 330:
        return 'bad'
    if degrees < 45:
        return 'warn'
    if degrees < 70:
        return 'warn'
    if degrees < 170:
        return 'ok'
    if degrees < 250:
        return 'accent'
    return 'violet'


def light_value(rgb, role, bucket):
    """명도를 뒤집어 라이트 값을 만든다. 글자는 대비 4.5:1을 만족할 때까지 더 내린다."""
    r, g, b = (component / 255 for component in rgb)
    h, l, s = colorsys.rgb_to_hls(r, g, b)

    if role == 'bg':
        # 어두운 배경일수록 밝은 배경이 된다. 채도는 낮춰 회색에 가깝게 둔다.
        target = 1.0 - l * 0.45
        target = min(0.99, max(0.86, target))
        saturation = min(s, 0.35)
    elif role == 'border':
        target = min(0.90, max(0.72, 1.0 - l * 0.75))
        saturation = min(s, 0.35)
    else:
        # 글자·아이콘. 밝은 색일수록 어두워진다.
        target = max(0.16, min(0.45, 1.0 - l))
        saturation = min(max(s, 0.10), 0.75)

    out = colorsys.hls_to_rgb(h, target, saturation)
    result = tuple(round(component * 255) for component in out)

    if role == 'fg':
        # 라이트 테마의 기본 표면(#ffffff ~ #f6f8fc) 위에서 4.5:1을 넘을 때까지 내린다.
        surface = (246, 248, 252)
        guard = 0
        while contrast(result, surface) < 4.5 and guard < 40:
            target = max(0.02, target - 0.02)
            out = colorsys.hls_to_rgb(h, target, saturation)
            result = tuple(round(component * 255) for component in out)
            guard += 1

    return result


def main():
    source = open(CSS, encoding='utf-8').read()
    lines = source.split('\n')

    # 테마 블록(:root, [data-theme=...])은 값이 정의되는 자리라 건드리지 않는다.
    in_theme = False
    roles = defaultdict(set)
    for line in lines:
        stripped = line.strip()
        if stripped.startswith(':root') or stripped.startswith('[data-theme="light"] {'):
            in_theme = True
        if in_theme:
            if stripped.endswith('}'):
                in_theme = False
            continue
        for match in PROP_RE.finditer(line):
            prop, value = match.group(1), match.group(2)
            role = 'bg' if prop.startswith('background') else 'border' if prop.startswith(('border', 'outline')) else 'fg'
            if prop == 'box-shadow':
                role = 'shadow'
            for hexv in HEX_RE.findall(value):
                roles[hexv.lower()].add(role)

    # 역할이 여럿이면 배경 > 테두리 > 글자 순으로 하나를 고른다(가장 눈에 띄는 쓰임을 따른다).
    chosen = {}
    for hexv, used in roles.items():
        chosen[hexv] = 'bg' if 'bg' in used else 'border' if 'border' in used else 'shadow' if 'shadow' in used else 'fg'

    # 이름을 정한다: 역할 + 색상군 + 밝기 순번.
    groups = defaultdict(list)
    for hexv, role in chosen.items():
        parsed = parse_hex(hexv)
        if parsed is None:
            continue
        rgb = parsed[:3]
        groups[(role, hue_bucket(rgb))].append((relative_luminance(rgb), hexv))

    names = {}
    dark_defs = []
    light_defs = []
    prefix = {'bg': 'surface', 'border': 'line', 'fg': 'ink', 'shadow': 'shadow'}
    for (role, bucket), items in sorted(groups.items()):
        items.sort()
        for index, (_, hexv) in enumerate(items, start=1):
            stem = prefix[role] if bucket == 'neutral' else f'{prefix[role]}-{bucket}'
            name = f'--{stem}-{index}'
            names[hexv] = name
            parsed = parse_hex(hexv)
            rgb, alpha = parsed[:3], parsed[3]
            dark_defs.append((name, hexv))
            light_defs.append((name, to_hex(light_value(rgb, role, bucket), alpha)))

    # 치환. 테마 블록은 건너뛴다.
    output = []
    in_theme = False
    for line in lines:
        stripped = line.strip()
        if stripped.startswith(':root') or stripped.startswith('[data-theme="light"] {'):
            in_theme = True
        if in_theme:
            if stripped.endswith('}'):
                in_theme = False
            output.append(line)
            continue

        def replace_declaration(match):
            prop, value = match.group(1), match.group(2)

            def replace_hex(hit):
                return f'var({names[hit.group(0).lower()]})' if hit.group(0).lower() in names else hit.group(0)

            return f'{prop}: {HEX_RE.sub(replace_hex, value)}'

        output.append(PROP_RE.sub(replace_declaration, line))

    text = '\n'.join(output)

    dark_block = '\n'.join(f'  {name}: {value};' for name, value in sorted(dark_defs))
    light_block = '\n'.join(f'  {name}: {value};' for name, value in sorted(light_defs))

    text = text.replace(
        '  --graph-node-border: #ffffff30;\n',
        '  --graph-node-border: #ffffff30;\n  --graph-node-bg: #1b2536;\n'
        '  /* 아래는 tools/themeVars.py가 만든 표면·테두리·글자·상태색이다. 값만 테마별로 다르다. */\n'
        + dark_block + '\n', 1)

    text = text.replace(
        '  --graph-node-border: #00000026;\n}',
        '  --graph-node-border: #00000026;\n  --graph-node-bg: #ffffff;\n' + light_block + '\n}', 1)

    open(CSS, 'w', encoding='utf-8').write(text)
    print(f'{len(names)} colors -> variables')


if __name__ == '__main__':
    main()
