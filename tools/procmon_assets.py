"""Lists the asset files a game client touched, from a Process Monitor CSV export.

Usage: python procmon_assets.py capture.csv [--process Main.exe] [--after HH:MM:SS] [--ext wav,bmd,ozj,ozt,ein]

Export from Procmon: File > Save > "Comma-Separated Values (CSV)", All events. Columns used: Time of Day, Process Name, Operation, Path.
--after keeps only events from that time of day on (use the minute you lowered the SD) so the startup preload is ignored.
"""
import argparse
import csv
import re
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('csv')
    ap.add_argument('--process', default='Main.exe')
    ap.add_argument('--after', default='')
    ap.add_argument('--ext', default='wav,bmd,ozj,ozt,ein,jpg,tga,bmp,ogg,mp3,smd')
    a = ap.parse_args()
    exts = tuple('.' + e.strip().lower() for e in a.ext.split(','))
    seen = {}
    with open(a.csv, newline='', encoding='utf-8-sig', errors='replace') as f:
        for row in csv.DictReader(f):
            if row.get('Process Name', '').lower() != a.process.lower():
                continue
            if row.get('Operation') not in ('CreateFile', 'ReadFile', 'QueryOpen', 'CreateFileMapping'):
                continue
            path = row.get('Path', '')
            if not path.lower().endswith(exts):
                continue
            t = row.get('Time of Day', '')
            m = re.match(r'(\d+):(\d+):(\d+)', t)
            if a.after and m and t.split('.')[0].zfill(8) < a.after.zfill(8):
                continue
            seen.setdefault(path, t)
    for path, t in sorted(seen.items(), key=lambda kv: kv[1]):
        print(f'{t}  {path}')
    print(f'\n{len(seen)} files', file=sys.stderr)


if __name__ == '__main__':
    main()
