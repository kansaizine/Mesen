#pragma once
#include "stdafx.h"
#include "BaseMapper.h"

class Mapper242 : public BaseMapper
{
protected:
	virtual uint16_t GetPRGPageSize() { return 0x8000; }
	virtual uint16_t GetCHRPageSize() { return 0x2000; }

	void InitMapper()
	{
		Reset(false);
		SelectCHRPage(0, 0);
	}

	virtual void Reset(bool softReset)
	{
		SelectPRGPage(0, 0);
		SetMirroringType(MirroringType::Vertical);
	}

	void WriteRegister(uint16_t addr, uint8_t value)
	{
		SetMirroringType(addr & 0x02 ? MirroringType::Horizontal : MirroringType::Vertical);
		SelectPRGPage(0, (addr >> 3) & 0x0F);
	}
};