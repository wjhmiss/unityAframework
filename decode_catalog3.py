import json, base64, struct

with open(r'D:\Unity\项目\test\Library\com.unity.addressables\aa\Windows\catalog.json', 'r', encoding='utf-8') as f:
    catalog = json.load(f)

key_data = base64.b64decode(catalog['m_KeyDataString'])

count = struct.unpack_from('<I', key_data, 0)[0]
print(f'Key count: {count}')

pos = 4
keys = []
for i in range(count):
    if pos >= len(key_data):
        break
    key_type = key_data[pos]
    pos += 1
    if pos + 4 > len(key_data):
        break
    length = struct.unpack_from('<I', key_data, pos)[0]
    pos += 4
    if pos + length > len(key_data):
        break
    key_text = key_data[pos:pos+length].decode('utf-8')
    keys.append(key_text)
    pos += length

print(f'Decoded {len(keys)} keys:')
for i, k in enumerate(keys):
    marker = ""
    if k == "PoolObjects": marker = " <=== POOLOBJECTS"
    if k == "bullet": marker = " <=== BULLET"
    print(f'  [{i}] "{k}"{marker}')

pool_idx = -1
bullet_idx = -1
for i, k in enumerate(keys):
    if k == "PoolObjects": pool_idx = i
    if k == "bullet": bullet_idx = i

print(f'\n"PoolObjects" key index: {pool_idx}')
print(f'"bullet" key index: {bullet_idx}')

bucket_data = base64.b64decode(catalog['m_BucketDataString'])
bcount = struct.unpack_from('<I', bucket_data, 0)[0]
print(f'\nBucket count: {bcount}')

bpos = 4
found_pool = False
for b in range(bcount):
    if bpos + 8 > len(bucket_data):
        break
    key_idx = struct.unpack_from('<i', bucket_data, bpos)[0]
    entry_count = struct.unpack_from('<i', bucket_data, bpos + 4)[0]
    bpos += 8
    
    key_text = keys[key_idx] if 0 <= key_idx < len(keys) else f'??? (idx={key_idx})'
    
    entries = []
    for e in range(entry_count):
        if bpos + 4 > len(bucket_data):
            break
        entry_idx = struct.unpack_from('<i', bucket_data, bpos)[0]
        bpos += 4
        entries.append(entry_idx)
    
    marker = ""
    if key_idx == pool_idx: marker = " <=== POOLOBJECTS BUCKET"
    print(f'  Bucket[{b}]: keyIdx={key_idx} (key="{key_text}"), entries={entries}{marker}')
    
    if key_idx == pool_idx:
        found_pool = True

print(f'\nPoolObjects bucket found: {found_pool}')
if not found_pool and pool_idx >= 0:
    print("PROBLEM: No bucket maps to the PoolObjects label!")
    print(f"PoolObjects key index is {pool_idx}, but no bucket references it.")
