"""Measure how well a vision model reads receipts and bank transfer screenshots.

Sends every image in images/ (or a folder you pass) to the model with the same prompt and
JSON schema the assistant uses (Logic/AiLogic.cs: BuildImagePrompt / BuildImageSchema), then
compares the answer with expected.json.

    python eval.py --url http://192.168.2.135:9292 --api openai
    python eval.py --url http://localhost:11434 --api ollama --model qwen2.5vl:7b
    python eval.py --url ... --images D:/my-photos      (no expected.json: prints what was read)

Only the standard library is needed; Pillow is used to resize like the browser does if installed.
"""
import argparse, base64, io, json, os, re, sys, time, unicodedata, urllib.request
from datetime import date

def build_prompt(today):
    here = os.path.dirname(os.path.abspath(__file__))
    src = open(os.path.join(here, '..', '..', 'Logic', 'AiLogic.cs'), encoding='utf-8').read()
    body = src[src.index('private static string BuildImagePrompt'):]
    body = body[:body.index('return prompt.ToString();')]
    lines = []
    for m in re.finditer(r'prompt\.AppendLine\((\$?)"((?:[^"\\]|\\.)*)"\)', body):
        text = m.group(2).replace('\\"', '"')
        if m.group(1):
            text = text.replace('{today:yyyy-MM-dd}', today.isoformat())
        lines.append(text)
    return '\n'.join(lines) + '\n'

def build_schema():
    text = {'type': 'string'}; num = {'type': 'number'}
    return {'type': 'object', 'properties': {
        'kind': {'type': 'string', 'enum': ['receipt', 'transfer', 'other']},
        'vendor': text, 'date': text, 'total': num, 'odometer': num, 'plate': text,
        'lines': {'type': 'array', 'items': {'type': 'object', 'properties': {'name': text, 'amount': num}, 'required': ['name', 'amount']}},
        'bank': text, 'reference': text, 'recipient': text, 'content': text},
        'required': ['kind', 'vendor', 'date', 'total', 'odometer', 'plate', 'lines', 'bank', 'reference', 'recipient', 'content']}

def load_image(path):
    data = open(path, 'rb').read()
    try:
        from PIL import Image
        img = Image.open(io.BytesIO(data)).convert('RGB')
        scale = min(1.0, 1600 / max(img.size))
        if scale < 1:
            img = img.resize((round(img.width * scale), round(img.height * scale)))
        buf = io.BytesIO(); img.save(buf, 'JPEG', quality=88); data = buf.getvalue()
        mime = 'image/jpeg'
    except ImportError:
        mime = 'image/png' if data[:4] == b'\x89PNG' else 'image/jpeg'
    return base64.b64encode(data).decode(), mime

def ask(args, prompt, schema, b64, mime):
    if args.api == 'openai':
        url = args.url.rstrip('/') + ('/chat/completions' if args.url.rstrip('/').endswith('/v1') else '/v1/chat/completions')
        req = {'model': args.model, 'stream': False, 'temperature': 0,
               'response_format': {'type': 'json_schema', 'json_schema': {'name': 'result', 'strict': True, 'schema': schema}},
               'messages': [{'role': 'system', 'content': prompt},
                            {'role': 'user', 'content': [{'type': 'image_url', 'image_url': {'url': f'data:{mime};base64,{b64}'}}, {'type': 'text', 'text': 'Đọc ảnh này.'}]}]}
    else:
        url = args.url.rstrip('/') + '/api/chat'
        req = {'model': args.model, 'stream': False, 'format': schema, 'options': {'temperature': 0},
               'messages': [{'role': 'system', 'content': prompt}, {'role': 'user', 'content': 'Đọc ảnh này.', 'images': [b64]}]}
    r = urllib.request.Request(url, json.dumps(req).encode(), {'Content-Type': 'application/json'})
    started = time.time()
    with urllib.request.urlopen(r, timeout=args.timeout) as resp:
        body = json.load(resp)
    content = body['choices'][0]['message']['content'] if args.api == 'openai' else body['message']['content']
    s, e = content.find('{'), content.rfind('}')
    return json.loads(content[s:e + 1]), time.time() - started

def norm(s):
    s = unicodedata.normalize('NFD', str(s or '')).encode('ascii', 'ignore').decode().lower()
    return re.sub(r'[^a-z0-9]', '', s)

def grade(got, exp):
    checks = {}
    for key in ('kind', 'date', 'total', 'odometer'):
        if key in exp:
            checks[key] = (float(got.get(key) or 0) == float(exp[key])) if key in ('total', 'odometer') else (str(got.get(key, '')) == str(exp[key]))
    for key in ('vendor', 'recipient'):
        if key in exp:
            checks[key] = norm(exp[key]) in norm(got.get(key))
    for key in ('plate', 'reference', 'content'):
        if key in exp:
            checks[key] = norm(exp[key]) == norm(got.get(key))
    if 'lines' in exp:
        checks['lines'] = len(got.get('lines') or []) == exp['lines']
    if 'no_account_numbers' in exp:
        dump = json.dumps(got)
        checks['no_account_numbers'] = not any(n in dump for n in exp['no_account_numbers'])
    return checks

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--url', required=True)
    p.add_argument('--api', choices=['openai', 'ollama'], default='openai')
    p.add_argument('--model', default='qwen2.5vl:7b')
    p.add_argument('--images', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), 'images'))
    p.add_argument('--timeout', type=int, default=300)
    p.add_argument('--out', default='results.json')
    args = p.parse_args()
    exp_path = os.path.join(args.images, '..', 'expected.json')
    expected = json.load(open(exp_path, encoding='utf-8')) if os.path.exists(exp_path) else {}
    prompt, schema = build_prompt(date(2026, 11, 11)), build_schema()
    results, passed, total = {}, 0, 0
    for name in sorted(os.listdir(args.images)):
        if not name.lower().endswith(('.jpg', '.jpeg', '.png')):
            continue
        b64, mime = load_image(os.path.join(args.images, name))
        try:
            got, secs = ask(args, prompt, schema, b64, mime)
        except Exception as ex:
            print(f'{name}: ERROR {ex}'); results[name] = {'error': str(ex)}; continue
        checks = grade(got, expected[name]) if name in expected else {}
        passed += sum(checks.values()); total += len(checks)
        results[name] = {'seconds': round(secs, 1), 'got': got, 'checks': checks}
        bad = [k for k, v in checks.items() if not v]
        print(f'{name}: {secs:.0f}s  {sum(checks.values())}/{len(checks)} ok' + (f'  wrong: {", ".join(bad)}' if bad else ''))
        print('   ', json.dumps(got, ensure_ascii=False))
    if total:
        print(f'\nField accuracy: {passed}/{total} = {100 * passed / total:.0f}%')
    json.dump(results, open(args.out, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)

if __name__ == '__main__':
    sys.exit(main())
