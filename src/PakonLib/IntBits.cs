// Pakon.IntBits
using PakonLib;
using PakonLib.Enums;

public class IntBits
{
    private int bits = 0;

    public int Bits
    {
        set
        {
            bits = value;
        }
    }

    public bool this[int nIndex]
    {
        get
        {
            return (bits & (1 << nIndex)) != 0;
        }
        set
        {
            if (value)
            {
                bits |= 1 << nIndex;
            }
            else
            {
                bits &= ~(1 << nIndex);
            }
        }
    }

    public bool this[ScannerCapabilities capability]
    {
        get
        {
            return this[(int)capability];
        }
        set
        {
            this[(int)capability] = value;
        }
    }

    public void Empty()
    {
        bits = 0;
    }

    public bool IsEmpty()
    {
        return bits == 0;
    }

    public void Equals(IntBits ib)
    {
        bits = ib.bits;
    }

    public IntBits Clone()
    {
        IntBits intBits = new IntBits();
        intBits.bits = bits;
        return intBits;
    }
}
