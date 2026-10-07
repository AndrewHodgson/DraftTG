-- Phase 9E.1 synthetic Arena card-database fixture. NOT Wizards' Raw_CardDatabase file.
-- Only the columns DraftTG reads, using the same names/types as Arena's Cards table.
-- GrpIds 86711-87099 carry the Order_* keys and coverage columns of the 17 WOE/WOT cards in the
-- P1P6/P1P7 ground-truth fixtures (Data 2026.63.0.270). GrpIds 900001-900005 are invented rows for
-- coverage and NULL-key handling; they do not correspond to real cards.
CREATE TABLE Versions (Type TEXT, Version TEXT);
INSERT INTO Versions VALUES ('Data', 'synthetic.1'), ('GRP', 'synthetic.1');

CREATE TABLE Cards (
  GrpId INT, ExpansionCode TEXT, CollectorNumber TEXT, DraftContent BOOLEAN, IsToken BOOLEAN, IsPrimaryCard BOOLEAN,
  Colors TEXT, Types TEXT, Supertypes TEXT, OldSchoolManaText TEXT,
  Order_LandLast INT, Order_ColorOrder INT, Order_CreaturesFirst INT, Order_CMCWithXLast INT,
  Order_Title TEXT, Order_MythicToCommon INT, Order_BasicLandsFirst INT);

INSERT INTO Cards (GrpId, ExpansionCode, CollectorNumber, DraftContent, IsToken, IsPrimaryCard, Colors, Types, Supertypes, OldSchoolManaText,
  Order_LandLast, Order_ColorOrder, Order_CreaturesFirst, Order_CMCWithXLast, Order_Title, Order_MythicToCommon, Order_BasicLandsFirst) VALUES
  (86982, 'WOE', '250', 1, 0, 1, '',  '1,2', '', 'o2',   0, 31, 0, 2,    'scarecrowguide',    3, 1),
  (86889, 'WOE', '181', 1, 0, 1, '5', '10',  '', 'o2oG', 0, 4,  1, 3,    'returnfromthewild', 3, 1),
  (86849, 'WOE', '147', 1, 0, 1, '4', '2',   '', 'o2oR', 0, 3,  0, 3,    'redcapthief',       3, 1),
  (86831, 'WOE', '133', 1, 0, 1, '4', '2',   '', 'o3oR', 0, 3,  0, 4,    'grabbygiant',       3, 1),
  (86840, 'WOE', '140', 1, 0, 1, '4', '2',   '', 'o2oR', 0, 3,  0, 3,    'merrybards',        3, 1),
  (86978, 'WOE', '246', 1, 0, 1, '',  '1,2', '', 'o1',   0, 31, 0, 1,    'gingerbrute',       3, 1),
  (86799, 'WOE', '103', 1, 0, 1, '3', '4',   '', 'oB',   0, 2,  1, 1,    'ratout',            3, 1),
  (86853, 'WOE', '151', 1, 0, 1, '4', '4',   '', 'oXoR', 0, 3,  1, 1001, 'stonesplitterbolt', 2, 1),
  (87058, 'WOT', '20',  1, 0, 1, '2', '3',   '', 'o1oU', 0, 1,  1, 2,    'hatchingplans',     2, 1),
  (86737, 'WOE', '50',  1, 0, 1, '2', '10',  '', 'o1oU', 0, 1,  1, 2,    'freezeinplace',     3, 1),
  (86813, 'WOE', '116', 1, 0, 1, '3', '2',   '', 'o2oB', 0, 2,  0, 3,    'voraciousvermin',   3, 1),
  (86851, 'WOE', '149', 1, 0, 1, '4', '2',   '', 'o1oR', 0, 3,  0, 2,    'skewerslinger',     3, 1),
  (86988, 'WOE', '256', 1, 0, 1, '',  '5',   '', '',     1, 31, 1, 0,    'evolvingwilds',     3, 1),
  (86750, 'WOE', '61',  1, 0, 1, '2', '4',   '', 'o3oU', 0, 1,  1, 4,    'misleadingmotes',   3, 1),
  (86711, 'WOE', '27',  1, 0, 1, '1', '2',   '', 'o3oW', 0, 0,  0, 4,    'rimefurreindeer',   3, 1),
  (86834, 'WOE', '135', 1, 0, 1, '4', '2',   '', 'oR',   0, 3,  0, 1,    'harriedspearguard', 3, 1),
  (87099, 'WOT', '61',  1, 0, 1, '5', '3',   '', 'o1oG', 0, 4,  1, 2,    'seasonofgrowth',    2, 1),
  (900001, 'WOE', '901', 1, 0, 1, '1',   '2', '',  'o2oWoW',  0, 0,    0, 4,    'syntheticmythic',   0,    1),
  (900002, 'WOE', '902', 1, 0, 1, '1,2', '2', '',  'o1oWoU',  0, 5,    0, 3,    'syntheticrare',     1,    1),
  (900003, 'WOE', '903', 1, 0, 1, '1,5', '2', '',  'o(G/W)o(G/W)', 0, 13, 0, 2, 'synthetichybrid',   2,    1),
  (900004, 'WOE', '904', 1, 0, 1, '',    '5', '1', '',        1, 4,    1, 0,    'syntheticforest',   4,    0),
  (900005, 'WOE', '',    0, 1, 0, '',    '2', '',  '',        NULL, NULL, NULL, NULL, NULL,          NULL, NULL);
