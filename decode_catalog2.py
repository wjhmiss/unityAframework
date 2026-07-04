import json, base64, struct

with open(r'D:\Unity\项目\test\Library\com.unity.addressables\aa\Windows\catalog.json', 'r', encoding='utf-8') as f:
    catalog = json.load(f)

# Decode key data
key_data = base64.b64decode(catalog['m_KeyDataString'])
print(f'Key data total bytes: {len(key_data)}')

# Find "PoolObjects" in key data
pool_target = b'PoolObjects'
bullet_target = b'bullet'

pool_offset = key_data.find(pool_target)
bullet_offset = key_data.find(bullet_target)
print(f'"PoolObjects" at offset: {pool_offset}')
print(f'"bullet" at offset: {bullet_offset}')

# Dump bytes around "PoolObjects"
if pool_offset >= 0:
    start = max(0, pool_offset - 30)
    end = min(len(key_data), pool_offset + len(pool_target) + 30)
    print(f'\nBytes around "PoolObjects" (offset {pool_offset}):')
    for i in range(start, end, 16):
        hex_str = ' '.join(f'{key_data[j]:02x}' for j in range(i, min(i+16, end)))
        ascii_str = ''.join(chr(key_data[j]) if 32 <= key_data[j] < 127 else '.' for j in range(i, min(i+16, end)))
        print(f'  {i:4d}: {hex_str:<48s} {ascii_str}')

# Dump bytes around "bullet"
if bullet_offset >= 0:
    start = max(0, bullet_offset - 30)
    end = min(len(key_data), bullet_offset + len(bullet_target) + 30)
    print(f'\nBytes around "bullet" (offset {bullet_offset}):')
    for i in range(start, end, 16):
        hex_str = ' '.join(f'{key_data[j]:02x}' for j in range(i, min(i+16, end)))
        ascii_str = ''.join(chr(key_data[j]) if 32 <= key_data[j] < 127 else '.' for j in range(i, min(i+16, end)))
        print(f'  {i:4d}: {hex_str:<48s} {ascii_str}')

# Dump first 100 bytes of key data to understand format
print(f'\nFirst 100 bytes of key data:')
for i in range(0, min(100, len(key_data)), 16):
    hex_str = ' '.join(f'{key_data[j]:02x}' for j in range(i, min(i+16, len(key_data))))
    ascii_str = ''.join(chr(key_data[j]) if 32 <= key_data[j] < 127 else '.' for j in range(i, min(i+16, len(key_data))))
    print(f'  {i:4d}: {hex_str:<48s} {ascii_str}')

# Now try to understand the key data format
# It might be: int32 count, then for each key: int32 dataLength, then key bytes
# Or: just concatenated length-prefixed strings
print(f'\nFirst 4 bytes as int32: {struct.unpack_from("<I", key_data, 0)[0]}')
print(f'Bytes 4-8 as int32: {struct.unpack_from("<I", key_data, 4)[0]}')

# Try format: int32 count, then for each: int32 length + string data
count = struct.unpack_from('<I', key_data, 0)[0]
print(f'\nTrying format: count={count}, then length-prefixed strings')
pos = 4
keys = []
for i in range(min(count, 100)):
    if pos + 4 > len(key_data):
        print(f'  Key[{i}]: Ran out of data at pos {pos}')
        break
    length = struct.unpack_from('<i', key_data, pos)[0]
    pos += 4
    if length < 0 or length > 10000:
        print(f'  Key[{i}]: Invalid length {length} at pos {pos-4}, stopping')
        break
    if pos + length > len(key_data):
        print(f'  Key[{i}]: Length {length} exceeds data at pos {pos}')
        break
    try:
        key_text = key_data[pos:pos+length].decode('utf-8')
    except:
        key_text = f'(decode error, {length} bytes)'
    keys.append(key_text)
    if 'Pool' in key_text or 'bullet' in key_text:
        print(f'  Key[{i}]: "{key_text}" (len={length})')
    pos += length

print(f'\nTotal keys decoded: {len(keys)}')
if len(keys) > 0:
    print(f'All keys:')
    for i, k in enumerate(keys):
        print(f'  [{i}] "{k}"')

# Now decode bucket data with the correct key indices
bucket_data = base64.b64decode(catalog['m_BucketDataString'])
print(f'\nBucket data total bytes: {len(bucket_data)}')

# Try format: int32 count, then for each: int32 keyIndex, int32 entryCount, int32[] entries
bcount = struct.unpack_from('<I', bucket_data, 0)[0]
print(f'Bucket count: {bcount}')

bpos = 4
for b in range(min(bcount, 100)):
    if bpos + 8 > len(bucket_data):
        print(f'  Bucket[{b}]: Ran out of data at pos {bpos}')
        break
    key_idx = struct.unpack_from('<i', bucket_data, bpos)[0]
    entry_count = struct.unpack_from('<i', bucket_data, bpos + 4)[0]
    bpos += 8
    
    if key_idx < 0 or key_idx >= len(keys):
        key_text = f'??? (idx={key_idx})'
    else:
        key_text = keys[key_idx]
    
    print(f'  Bucket[{b}]: keyIdx={key_idx} (key="{key_text}"), entryCount={entry_count}')
    
    for e in range(min(entry_count, 100)):
        if bpos + 4 > len(bucket_data):
            break
        entry_idx = struct.unpack_from('<i', bucket_data, bpos)[0]
        bpos += 4
        print(f'    entry[{e}] = {entry_idx}')
