import json, base64, struct

with open(r'D:\Unity\项目\test\Library\com.unity.addressables\aa\Windows\catalog.json', 'r', encoding='utf-8') as f:
    catalog = json.load(f)

# Decode key data
key_data = base64.b64decode(catalog['m_KeyDataString'])
print(f'Key data total bytes: {len(key_data)}')

# Read key count
key_count = struct.unpack_from('<i', key_data, 0)[0]
print(f'Key count: {key_count}')

# Read offset/length pairs
pos = 4
offsets = []
lengths = []
for i in range(key_count):
    offset = struct.unpack_from('<i', key_data, pos)[0]
    length = struct.unpack_from('<i', key_data, pos + 4)[0]
    offsets.append(offset)
    lengths.append(length)
    pos += 8

# Key data starts after the offset/length array
key_data_start = 4 + key_count * 8
print(f'Key data section starts at: {key_data_start}')

# Decode keys
keys = []
for i in range(key_count):
    start = key_data_start + offsets[i]
    length = lengths[i]
    if start >= 0 and start + length <= len(key_data) and length > 0:
        try:
            key_text = key_data[start:start+length].decode('utf-8')
        except:
            key_text = f'(decode error at {start}+{length})'
    else:
        key_text = f'(OUT OF RANGE: start={start}, len={length}, data_len={len(key_data)})'
    keys.append(key_text)
    if 'Pool' in key_text or 'bullet' in key_text or 'prefab' in key_text:
        print(f'  Key[{i}]: "{key_text}" (offset={offsets[i]}, length={lengths[i]})')

# Decode bucket data
bucket_data = base64.b64decode(catalog['m_BucketDataString'])
print(f'\nBucket data total bytes: {len(bucket_data)}')
bucket_count = struct.unpack_from('<i', bucket_data, 0)[0]
print(f'Bucket count: {bucket_count}')

bpos = 4
for b in range(bucket_count):
    if bpos + 8 > len(bucket_data):
        break
    key_idx = struct.unpack_from('<i', bucket_data, bpos)[0]
    entry_count = struct.unpack_from('<i', bucket_data, bpos + 4)[0]
    bpos += 8
    
    key_text = keys[key_idx] if 0 <= key_idx < len(keys) else '???'
    print(f'  Bucket[{b}]: keyIdx={key_idx} (key="{key_text}"), entryCount={entry_count}')
    
    for e in range(entry_count):
        if bpos + 4 > len(bucket_data):
            break
        entry_idx = struct.unpack_from('<i', bucket_data, bpos)[0]
        bpos += 4
        print(f'    entry[{e}] = {entry_idx}')

# Print ALL keys for reference
print(f'\n=== ALL KEYS ===')
for i, k in enumerate(keys):
    print(f'  [{i}] "{k}"')
