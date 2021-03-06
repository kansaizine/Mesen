#pragma once
#include "stdafx.h"
#include <unordered_map>
using std::unordered_map;

class BaseMapper;
enum class AddressType;

class LabelManager
{
private:
	unordered_map<uint32_t, string> _codeLabels;
	unordered_map<string, uint32_t> _codeLabelReverseLookup;
	unordered_map<uint32_t, string> _codeComments;

	shared_ptr<BaseMapper> _mapper;

	int32_t GetLabelAddress(uint16_t relativeAddr, bool checkRegisters);

public:
	LabelManager(shared_ptr<BaseMapper> mapper);

	void SetLabel(uint32_t address, AddressType addressType, string label, string comment);
	
	int32_t GetLabelRelativeAddress(string label);

	string GetLabel(uint16_t relativeAddr, bool checkRegisters);
	string GetComment(uint16_t relativeAddr);
};
