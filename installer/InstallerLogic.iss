// Shared by production setup and the install-free native verification harness.
type
  TReleaseParts = array[0..2] of Integer;
  TNativeFindData = record
    Attributes: LongWord;
    CreationTime, LastAccessTime, LastWriteTime: TFileTime;
    SizeHigh, SizeLow, Reserved0, Reserved1: LongWord;
    Name: array[0..259] of Char;
    AlternateName: array[0..13] of Char;
  end;

function TryReleaseParts(Value: String; var Parts: TReleaseParts): Boolean;
var
  I, Cut, Component, Count: Integer;
  Token, Core: String;
begin
  Result := False;
  Core := Trim(Value);
  Cut := Pos('-', Core);
  if Cut > 0 then Core := Copy(Core, 1, Cut - 1);
  Cut := Pos('+', Core);
  if Cut > 0 then Core := Copy(Core, 1, Cut - 1);
  for I := 0 to 2 do Parts[I] := 0;
  Count := 0;
  while Core <> '' do
  begin
    Cut := Pos('.', Core);
    if Cut = 0 then Cut := Length(Core) + 1;
    Token := Copy(Core, 1, Cut - 1);
    if Token = '' then exit;
    for I := 1 to Length(Token) do
      if (Token[I] < '0') or (Token[I] > '9') then exit;
    Component := StrToIntDef(Token, -1);
    if (Component < 0) or (Component > 65535) or (Count > 3) then exit;
    if Count < 3 then Parts[Count] := Component;
    Count := Count + 1;
    // A trailing dot is invalid, rather than an implicit zero component.
    if Cut = Length(Core) then exit;
    Delete(Core, 1, Cut);
  end;
  Result := Count >= 2;
end;

function TryCompareReleases(const Left, Right: String; var Comparison: Integer): Boolean;
var
  A, B: TReleaseParts;
  I: Integer;
begin
  Result := TryReleaseParts(Left, A) and TryReleaseParts(Right, B);
  Comparison := 0;
  if not Result then exit;
  for I := 0 to 2 do
  begin
    if A[I] < B[I] then begin Comparison := -1; exit; end;
    if A[I] > B[I] then begin Comparison := 1; exit; end;
  end;
end;

function SkipQuickUpdatePage(QuickUpdate: Boolean; PageID: Integer): Boolean;
begin
  Result := QuickUpdate and ((PageID = wpWelcome) or (PageID = wpSelectDir) or
    (PageID = wpSelectComponents) or (PageID = wpSelectProgramGroup) or (PageID = wpSelectTasks));
end;

function NativeFileAttributes(Name: String): LongWord;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function NativeFindFirst(Name: String; var Data: TNativeFindData): THandle;
  external 'FindFirstFileW@kernel32.dll stdcall';
function NativeFindNext(Handle: THandle; var Data: TNativeFindData): Boolean;
  external 'FindNextFileW@kernel32.dll stdcall';
function NativeFindClose(Handle: THandle): Boolean;
  external 'FindClose@kernel32.dll stdcall';

function FoundName(const Data: TNativeFindData): String;
var I: Integer;
begin
  Result := '';
  for I := 0 to 259 do
  begin
    if Data.Name[I] = #0 then exit;
    Result := Result + Data.Name[I];
  end;
end;

// Check ancestors as well: a plain child of a redirected parent must not be traversed.
function HasPlainAncestors(Name: String): Boolean;
var
  Parent: String;
  Attributes: LongWord;
begin
  Result := False;
  Name := RemoveBackslashUnlessRoot(ExpandFileName(Name));
  while Name <> '' do
  begin
    Attributes := NativeFileAttributes(Name);
    if (Attributes = $FFFFFFFF) or ((Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0) then exit;
    Parent := ExtractFileDir(Name);
    if CompareText(Parent, Name) = 0 then break;
    Name := Parent;
  end;
  Result := True;
end;

function IsPlainTree(const Name: String; Depth: Integer): Boolean;
var
  Entry: TNativeFindData;
  Handle: THandle;
  ChildName: String;
begin
  Result := False;
  if (Depth > 64) or not HasPlainAncestors(Name) then exit;
  Handle := NativeFindFirst(AddBackslash(Name) + '*', Entry);
  if Handle = THandle(-1) then
  begin
    // FindFirst can fail on an empty directory, but access failures must fail closed.
    Result := DLLGetLastError() = 2;
    exit;
  end;
  try
    repeat
      ChildName := FoundName(Entry);
      if (ChildName <> '.') and (ChildName <> '..') then
      begin
        if (Entry.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then exit;
        if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          if not IsPlainTree(AddBackslash(Name) + ChildName, Depth + 1) then exit;
      end;
    until not NativeFindNext(Handle, Entry);
    Result := DLLGetLastError() = 18; // ERROR_NO_MORE_FILES
  finally
    NativeFindClose(Handle);
  end;
end;

function IsSafeProfileTarget(const Name, AppDataRoot: String): Boolean;
begin
  Result := (CompareText(RemoveBackslashUnlessRoot(ExpandFileName(Name)),
    AddBackslash(RemoveBackslashUnlessRoot(ExpandFileName(AppDataRoot))) + 'Paperbunkr') = 0)
    and IsPlainTree(Name, 0);
end;

// No recursive DelTree: recheck each child immediately before deletion, never traverse links.
function RemovePlainTree(const Name: String): Boolean;
var
  Entry: TNativeFindData;
  Handle: THandle;
  Child, ChildName: String;
begin
  Result := False;
  if not HasPlainAncestors(Name) then exit;
  Handle := NativeFindFirst(AddBackslash(Name) + '*', Entry);
  if Handle <> THandle(-1) then
  begin
    try
      repeat
        ChildName := FoundName(Entry);
        if (ChildName <> '.') and (ChildName <> '..') then
        begin
          Child := AddBackslash(Name) + ChildName;
          if not HasPlainAncestors(Child) then exit;
          if (Entry.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            if not RemovePlainTree(Child) then exit;
          end
          else if not DeleteFile(Child) then exit;
        end;
      until not NativeFindNext(Handle, Entry);
      if DLLGetLastError() <> 18 then exit;
    finally
      NativeFindClose(Handle);
    end;
  end
  else if DLLGetLastError() <> 2 then exit;
  Result := RemoveDir(Name);
end;
