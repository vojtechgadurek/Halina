using System;

namespace Halina.Core;
public enum Nucleotide : byte
{
    A = 0,
    C = 1,
    G = 2,
    T = 3
}

public class Kmer : IXorType<Kmer>
{
    private readonly byte[] _data;
    private readonly int _length;

    public int Length => _length;

    public Kmer(int length)
    {
        if (length <= 0) throw new ArgumentException("Length must be positive", nameof(length));
        _length = length;
        // 2 bits per nucleotide
        int byteCount = (length * 2 + 7) / 8;
        _data = new byte[byteCount];
    }

    public Kmer(string sequence) : this(sequence.Length)
    {
        for (int i = 0; i < sequence.Length; i++)
        {
            SetNucleotide(i, CharToNucleotide(sequence[i]));
        }
    }

    public Kmer(Nucleotide[] nucleotides) : this(nucleotides.Length)
    {
        for (int i = 0; i < nucleotides.Length; i++)
        {
            SetNucleotide(i, nucleotides[i]);
        }
    }

    private Kmer(byte[] data, int length)
    {
        _data = data;
        _length = length;
    }

    public static Kmer FromString(string s)
    {
        return new Kmer(s);
    }

    public override string ToString()
    {
        char[] chars = new char[_length];
        for (int i = 0; i < _length; i++)
        {
            chars[i] = NucleotideToChar(GetNucleotide(i));
        }
        return new string(chars);
    }

    public byte[] GetBytes()
    {
        return _data;
    }

    public Kmer DeepCopy()
    {
        byte[] newData = new byte[_data.Length];
        Array.Copy(_data, newData, _data.Length);
        return new Kmer(newData, _length);
    }

    public void ShiftLeft(Nucleotide n)
    {
        // Shift bits left by 2 (towards lower index / higher significance)
        // This corresponds to appending at the end of the sequence
        
        for (int i = 0; i < _data.Length - 1; i++)
        {
            _data[i] = (byte)((_data[i] << 2) | (_data[i + 1] >> 6));
        }
        _data[_data.Length - 1] <<= 2;

        // Insert new nucleotide at the last position
        SetNucleotide(_length - 1, n);
    }

    public void ShiftRight(Nucleotide n)
    {
        // Shift bits right by 2 (towards higher index / lower significance)
        // This corresponds to prepending at the start of the sequence

        for (int i = _data.Length - 1; i > 0; i--)
        {
            _data[i] = (byte)((_data[i] >> 2) | (_data[i - 1] << 6));
        }
        _data[0] >>= 2;

        // Insert new nucleotide at the first position
        _data[0] |= (byte)((byte)n << 6);

        // Clear unused bits in the last byte
        int usedBitsInLastByte = (_length * 2) % 8;
        if (usedBitsInLastByte != 0)
        {
            byte mask = (byte)(0xFF << (8 - usedBitsInLastByte));
            _data[_data.Length - 1] &= mask;
        }
    }

    public void SetNucleotide(int index, Nucleotide n)
    {
        int bitOffset = index * 2;
        int byteIndex = bitOffset / 8;
        int shift = 6 - (bitOffset % 8);
        _data[byteIndex] = (byte)((_data[byteIndex] & ~(3 << shift)) | ((byte)n << shift));
    }

    public Nucleotide GetNucleotide(int index)
    {
        int bitOffset = index * 2;
        int byteIndex = bitOffset / 8;
        int shift = 6 - (bitOffset % 8);
        return (Nucleotide)((_data[byteIndex] >> shift) & 3);
    }

    public static Nucleotide CharToNucleotide(char c) => c switch
    {
        'A' or 'a' => Nucleotide.A,
        'C' or 'c' => Nucleotide.C,
        'G' or 'g' => Nucleotide.G,
        'T' or 't' => Nucleotide.T,
        _ => throw new ArgumentException($"Invalid nucleotide character: {c}")
    };

    public static char NucleotideToChar(Nucleotide n) => n switch
    {
        Nucleotide.A => 'A',
        Nucleotide.C => 'C',
        Nucleotide.G => 'G',
        Nucleotide.T => 'T',
        _ => throw new ArgumentException($"Invalid nucleotide value: {n}")
    };

    public Kmer Xor(Kmer other)
    {
        if (other == null || other._length != _length) throw new ArgumentException("Cannot XOR Kmers of different lengths or null");
        byte[] newData = new byte[_data.Length];
        for (int i = 0; i < _data.Length; i++)
        {
            newData[i] = (byte)(_data[i] ^ other._data[i]);
        }
        return new Kmer(newData, _length);
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Kmer other) return false;
        if (_length != other._length) return false;
        if (_data.Length != other._data.Length) return false;
        for (int i = 0; i < _data.Length; i++)
        {
            if (_data[i] != other._data[i]) return false;
        }
        return true;
    }
}