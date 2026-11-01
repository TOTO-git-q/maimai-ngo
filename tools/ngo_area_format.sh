#!/usr/bin/env bash
# Stock format study of the area-select textures (see ngo_stock_format.py).
cd "/e/claude workspace/maimai-partner-live2d"
python tools/ngo_stock_format.py /e/cxref/maimai-ngo/tasks/refs_area/stock_format \
  UI_CHS_BG_Parts_01 UI_CHS_FrameBase_03 UI_CHS_SelecterBase_02 UI_CHS_SelectorBase_Open_S \
  UI_CMN_Btn_GotoCollaboArea UI_CMN_Btn_Mission_01 UI_CMN_Frame_01 UI_CMN_InfoBase_02 \
  UI_CMN_SelectorBase_01 UI_CMN_SelectorBase_01_Shadow UI_CMN_SelectorBase_02 \
  UI_CMN_Selector_TitleBase_01 UI_CMN_Selector_TitleBase_02 UI_CMN_Selector_TitleBase_Shadow \
  UI_CMN_Setting_01_Btn UI_MDS_Btn_Arrow UI_CHS_Countryside_FrameBase UI_CHS_Title_03 2>&1 | grep -v Warning
