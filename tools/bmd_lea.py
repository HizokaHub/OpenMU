"""LEA (Lightweight Encryption Algorithm) de 128/256 bits, solo lo necesario para los BMD 0x0E/0x0F de clientes IGCN."""
import struct

M = 0xFFFFFFFF
DELTA = [0xC3EFE9DB, 0x44626B02, 0x79E27C8A, 0x78DF30EC, 0x715EA49E, 0xC785DA0A, 0xE04EF22A, 0xE5C40957]


def rol(x, n):
    n &= 31
    return ((x << n) | (x >> (32 - n))) & M if n else x


def ror(x, n):
    return rol(x, 32 - (n & 31))


def key_schedule(key):
    words = list(struct.unpack('<%dI' % (len(key) // 4), key))
    if len(key) == 16:
        rounds, t = 24, words
        rks = []
        for i in range(rounds):
            d = DELTA[i % 4]
            t[0] = rol((t[0] + rol(d, i)) & M, 1)
            t[1] = rol((t[1] + rol(d, i + 1)) & M, 3)
            t[2] = rol((t[2] + rol(d, i + 2)) & M, 6)
            t[3] = rol((t[3] + rol(d, i + 3)) & M, 11)
            rks.append([t[0], t[1], t[2], t[1], t[3], t[1]])
        return rks
    if len(key) == 32:
        rounds, t = 32, words
        rks = []
        for i in range(rounds):
            d = DELTA[i % 8]
            rk = []
            for j, rot in enumerate((1, 3, 6, 11, 13, 17)):
                idx = (6 * i + j) % 8
                t[idx] = rol((t[idx] + rol(d, i + j)) & M, rot)
                rk.append(t[idx])
            rks.append(rk)
        return rks
    raise ValueError('clave de 16 o 32 bytes')


def encrypt_block(block, rks):
    x = list(struct.unpack('<4I', block))
    for rk in rks:
        n0 = rol(((x[0] ^ rk[0]) + (x[1] ^ rk[1])) & M, 9)
        n1 = ror(((x[1] ^ rk[2]) + (x[2] ^ rk[3])) & M, 5)
        n2 = ror(((x[2] ^ rk[4]) + (x[3] ^ rk[5])) & M, 3)
        x = [n0, n1, n2, x[0]]
    return struct.pack('<4I', *x)


def decrypt_block(block, rks):
    x = list(struct.unpack('<4I', block))
    for rk in reversed(rks):
        p0 = x[3]
        p1 = ((ror(x[0], 9) - (p0 ^ rk[0])) & M) ^ rk[1]
        p2 = ((rol(x[1], 5) - (p1 ^ rk[2])) & M) ^ rk[3]
        p3 = ((rol(x[2], 3) - (p2 ^ rk[4])) & M) ^ rk[5]
        x = [p0, p1, p2, p3]
    return struct.pack('<4I', *x)


def decrypt_ecb(data, key):
    rks = key_schedule(key)
    return b''.join(decrypt_block(data[i:i + 16], rks) for i in range(0, len(data) - len(data) % 16, 16))


if __name__ == '__main__':
    # Vector de prueba oficial de LEA-128.
    k = bytes.fromhex('0f1e2d3c4b5a69788796a5b4c3d2e1f0')
    p = bytes.fromhex('101112131415161718191a1b1c1d1e1f')
    c = encrypt_block(p, key_schedule(k))
    print('LEA-128 cifrado:', c.hex(), 'OK' if c.hex() == '9fc84e3528c6c6185532c7a704648bfd' else 'MAL')
    print('LEA-128 descifrado OK' if decrypt_block(c, key_schedule(k)) == p else 'descifrado MAL')
