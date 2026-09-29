; *** Inno Setup バージョン 6.5.0+ 日本語メッセージ ***
;
; このファイルのユーザー貢献翻訳をダウンロードするには、以下を参照してください:
;    https://jrsoftware.org/files/istrans/
;
; 注意: このテキストを翻訳する際、既にピリオド (.)が付いていないメッセージの
; 末尾にピリオドを追加しないでください。Inno Setup がそれらのメッセージに
; 自動的にピリオドを追加するためです (ピリオドを追加すると二重に表示されます)。
;
; 最終更新: 2026.09.29
; 翻訳 : coolvitto
;
[LangOptions]
; 以下の3つの項目は非常に重要です。ヘルプファイルの
; 「[LangOptions] セクション」トピックを必ず読んで理解してください。
LanguageName=日本語
LanguageID=$0411
; LanguageCodePage は可能な限り常に設定してください。このファイルが Unicode であっても同様です
; 英語の場合は ASCII 文字のみを使用するため、とにかくゼロに設定されます
LanguageCodePage=932
; 翻訳先の言語が特別なフォントフェイスやサイズを必要とする場合は、
; 以下の項目のコメントを解除して適宜変更してください。
;DialogFontName=
;DialogFontSize=9
;DialogFontBaseScaleWidth=7
;DialogFontBaseScaleHeight=15
;WelcomeFontName=Segoe UI
;WelcomeFontSize=14

[Messages]

; *** アプリケーションタイトル
SetupAppTitle=セットアップ
SetupWindowTitle=%1 のセットアップ
UninstallAppTitle=アンインストール
UninstallAppFullTitle=%1 のアンインストール

; *** その他共通
InformationTitle=情報
ConfirmTitle=確認
ErrorTitle=エラー

; *** SetupLdr メッセージ
SetupLdrStartupMessage=%1 をインストールします。%n%n続行しますか？
LdrCannotCreateTemp=一時ファイルを作成できません。%n%nセットアップを中止しました。
LdrCannotExecTemp=一時フォルダー内のファイルを実行できません。%n%nセットアップを中止しました。
HelpTextNote=

; *** 起動時エラーメッセージ
LastErrorMessage=%1.%n%nエラー %2: %3
SetupFileMissing=セットアップフォルダーにファイル %1 が見つかりません。%n%n問題を修正するか、プログラムの新しいコピーを入手してください。
SetupFileCorrupt=セットアップファイルが破損しています。%n%nプログラムの新しいコピーを入手してください。
SetupFileCorruptOrWrongVer=セットアップファイルが破損しているか、このバージョンのセットアッププログラムと互換性がありません。%n%n問題を修正するか、プログラムの新しいコピーを入手してください。
InvalidParameter=コマンドラインに無効なパラメータが指定されました:%n%n%1
SetupAlreadyRunning=セットアップは既に実行中です。
WindowsVersionNotSupported=このプログラムは、コンピュータにインストールされている Windows のバージョンをサポートしていません。
WindowsServicePackRequired=このプログラムには %1 Service Pack %2 以降が必要です。
NotOnThisPlatform=このプログラムは %1 と互換性がありません。
OnlyOnThisPlatform=このプログラムには %1 が必要です。
OnlyOnTheseArchitectures=このプログラムは、以下の CPU アーキテクチャ用に設計された Windows のバージョンにのみインストールできます:%n%n%1
WinVersionTooLowError=このプログラムには %1 バージョン %2 以降が必要です。
WinVersionTooHighError=このプログラムは %1 バージョン %2 以降にはインストールできません。
AdminPrivilegesRequired=このプログラムをインストールするには管理者権限が必要です。
PowerUserPrivilegesRequired=このプログラムをインストールするには管理者または Power Users の権限が必要です。
SetupAppRunningError=セットアップは %1 が現在実行中であることを検出しました。%n%nプログラムのすべてのインスタンスを閉じてから、「OK」を選択して続行するか、「キャンセル」を選択して終了してください。
UninstallAppRunningError=アンインストールは %1 が現在実行中であることを検出しました。%n%nプログラムのすべてのインスタンスを閉じてから、「OK」を選択して続行するか、「キャンセル」を選択して終了してください。

; *** 起動時の質問
PrivilegesRequiredOverrideTitle=インストールモードの選択
PrivilegesRequiredOverrideInstruction=インストールモードを選択してください
PrivilegesRequiredOverrideText1=%1 はすべてのユーザー用 (管理者権限が必要)または現在のユーザーのみにインストールできます。
PrivilegesRequiredOverrideText2=%1 は現在のユーザーのみ、またはすべてのユーザー用 (管理者権限が必要)にインストールできます。
PrivilegesRequiredOverrideAllUsers=すべてのユーザー用にインストール(&A)
PrivilegesRequiredOverrideAllUsersRecommended=すべてのユーザー用にインストール(&A) (推奨)
PrivilegesRequiredOverrideCurrentUser=現在のユーザーのみにインストール(&U)
PrivilegesRequiredOverrideCurrentUserRecommended=現在のユーザーのみにインストール(&U) (推奨)

; *** その他エラー
ErrorCreatingDir=フォルダー "%1" を作成できません
ErrorTooManyFilesInDir=フォルダー "%1" にファイルを作成できません。ファイルが多すぎます

; *** セットアップ共通メッセージ
ExitSetupTitle=セットアップの終了
ExitSetupMessage=セットアップが完了していません。%n%n今セットアップを終了すると、プログラムはインストールされません。%n%n後でセットアップを実行できます。%n%nセットアップを終了しますか？
AboutSetupMenuItem=セットアップについて(&A)...
AboutSetupTitle=セットアップについて
AboutSetupMessage=%1 %2%n%3%n%n%1 Web サイト:%n%4
AboutSetupNote=
TranslatorNote=日本語翻訳

; *** ボタン
ButtonBack=< 戻る(&B)
ButtonNext=次へ(&N) >
ButtonInstall=インストール(&I)
ButtonOK=OK
ButtonCancel=キャンセル
ButtonYes=はい(&Y)
ButtonYesToAll=すべてはい(&T)
ButtonNo=いいえ(&N)
ButtonNoToAll=すべていいえ(&O)
ButtonFinish=完了(&F)
ButtonBrowse=参照(&B)...
ButtonWizardBrowse=参照(&F)...
ButtonNewFolder=新しいフォルダーを作成(&C)

; *** 「言語の選択」ダイアログメッセージ
SelectLanguageTitle=セットアップ言語の選択
SelectLanguageLabel=セットアップ中に使用する言語を選択してください。

; *** 共通ウィザードテキスト
ClickNext=続行するには「次へ」を選択するか、「キャンセル」を選択して終了してください。
BeveledLabel=
BrowseDialogTitle=フォルダーの参照
BrowseDialogLabel=一覧からフォルダーを選択し、「OK」を選択してください。
NewFolderName=新しいフォルダー

; *** 「ようこそ」ウィザードページ
WelcomeLabel1=[name] のセットアップ
WelcomeLabel2=コンピュータに [name/ver] をインストールします。%n%n続行する前に、すべての実行中のアプリケーションを閉じてください。

; *** 「パスワード」ウィザードページ
WizardPassword=パスワード
PasswordLabel1=このセットアップはパスワードで保護されています。
PasswordLabel3=パスワードを入力し、続行するには「次へ」を選択してください。%nパスワードは大文字と小文字を区別します。
PasswordEditLabel=パスワード(&P):
IncorrectPassword=入力したパスワードが正しくありません。もう一度試してください。

; *** 「ライセンス契約」ウィザードページ
WizardLicense=ライセンス契約
LicenseLabel=続行する前に、以下の情報をよくお読みください。
LicenseLabel3=以下のライセンス契約をお読みください。%nセットアップを続行するには、契約のすべての条件に同意する必要があります。
LicenseAccepted=ライセンス契約の条件に同意します(&C)
LicenseNotAccepted=ライセンス契約の条件に同意しません(&N)

; *** 「情報」ウィザードページ
WizardInfoBefore=情報
InfoBeforeLabel=続行する前に、以下の重要な情報をお読みください。
InfoBeforeClickLabel=続行する準備ができたら「次へ」を選択してください。
WizardInfoAfter=情報
InfoAfterLabel=続行する前に、以下の重要な情報をお読みください。
InfoAfterClickLabel=続行する準備ができたら「次へ」を選択してください。

; *** 「ユーザー情報」ウィザードページ
WizardUserInfo=ユーザー情報
UserInfoDesc=以下の情報を入力してください。
UserInfoName=名前(&N):
UserInfoOrg=会社名(&O):
UserInfoSerial=シリアル番号(&S):
UserInfoNameRequired=名前を入力する必要があります。

; *** 「インストール先の選択」ウィザードページ
WizardSelectDir=インストール先フォルダーの選択
SelectDirDesc=[name] をどこにインストールしますか？
SelectDirLabel3=[name] は以下のフォルダーにインストールされます。
SelectDirBrowseLabel=続行するには「次へ」を選択してください。%n別のフォルダーを選択するには「参照」を選択してください。
DiskSpaceGBLabel=ディスクに少なくとも [gb] GB の空き領域が必要です。
DiskSpaceMBLabel=ディスクに少なくとも [mb] MB の空き領域が必要です。
CannotInstallToNetworkDrive=ネットワークドライブにインストールすることはできません。
CannotInstallToUNCPath=UNC パスにインストールすることはできません。
InvalidPath=ドライブ文字を含む完全なパスを入力する必要があります。例:%n%nC:\APP%n%nまたは以下の形式のネットワークパス:%n%n\\server\share
InvalidDrive=選択したドライブまたはネットワークパスが存在しないか、アクセスできません。%n%n別のドライブまたはパスを選択してください。
DiskSpaceWarningTitle=ディスクの空き領域が不足しています
DiskSpaceWarning=セットアップを実行するには少なくとも %1 KB の空き領域が必要ですが、選択したドライブには %2 KB しか空きがありません。%n%n続行しますか？
DirNameTooLong=フォルダー名またはパスが長すぎます。
InvalidDirName=フォルダー名が無効です。
BadDirName32=フォルダー名に以下の文字を含めることはできません:%n%n%1
DirExistsTitle=フォルダーが既に存在します
DirExists=フォルダー%n%n%1%n%nは既に存在します。%n%nこのフォルダーにアプリケーションをインストールしますか？
DirDoesntExistTitle=フォルダーが存在しません
DirDoesntExist=フォルダー%n%n%1%n%nは存在しません。フォルダーを作成しますか？

; *** 「コンポーネントの選択」ウィザードページ
WizardSelectComponents=コンポーネントの選択
SelectComponentsDesc=どのコンポーネントをインストールしますか？
SelectComponentsLabel2=インストールするコンポーネントを選択し、インストールしないものの選択を解除してください。%n続行するには「次へ」を選択してください。
FullInstallation=完全インストール
; 可能であれば「Compact」を「Minimal」と訳さないでください (「Minimal」はあなたの言語で)
CompactInstallation=コンパクトインストール
CustomInstallation=カスタムインストール
NoUninstallWarningTitle=既存のコンポーネント
NoUninstallWarning=セットアップは以下のコンポーネントが既にコンピュータにインストールされていることを検出しました:%n%n%1%n%nこれらのコンポーネントの選択を解除しても削除されません。%n%n続行しますか？
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=現在の選択には少なくとも [gb] GB のディスク領域が必要です。
ComponentsDiskSpaceMBLabel=現在の選択には少なくとも [mb] MB のディスク領域が必要です。

; *** 「追加タスクの選択」ウィザードページ
WizardSelectTasks=追加タスクの選択
SelectTasksDesc=どの追加タスクを実行しますか？
SelectTasksLabel2=[name] のインストール中に実行する追加タスクを選択し、「次へ」を選択してください。

; *** 「スタートメニューフォルダーの選択」ウィザードページ
WizardSelectProgramGroup=スタートメニューフォルダーの選択
SelectStartMenuFolderDesc=プログラムへのショートカットをどこに配置しますか？
SelectStartMenuFolderLabel3=プログラムへのショートカットは以下のスタートメニューフォルダーに作成されます。
SelectStartMenuFolderBrowseLabel=続行するには「次へ」を選択してください。%n別のフォルダーを選択するには「参照」を選択してください。
MustEnterGroupName=フォルダー名を入力する必要があります。
GroupNameTooLong=フォルダー名またはパスが長すぎます。
InvalidGroupName=フォルダー名が無効です。
BadGroupName=フォルダー名に以下の文字を含めることはできません:%n%n%1
NoProgramGroupCheck2=スタートメニューフォルダーを作成しない(&D)

; *** 「インストールの準備完了」ウィザードページ
WizardReady=インストールの準備完了
ReadyLabel1=コンピュータに [name] をインストールする準備ができました。
ReadyLabel2a=インストールを続行するには「インストール」を選択するか、設定を確認・変更するには「戻る」を選択してください。
ReadyLabel2b=インストールを続行するには「インストール」を選択してください。
ReadyMemoUserInfo=ユーザー情報:
ReadyMemoDir=インストール先フォルダー:
ReadyMemoType=セットアップの種類:
ReadyMemoComponents=選択したコンポーネント:
ReadyMemoGroup=スタートメニューフォルダー:
ReadyMemoTasks=追加タスク:

; *** TDownloadWizardPage ウィザードページと DownloadTemporaryFile
DownloadingLabel2=ファイルをダウンロード中...
ButtonStopDownload=ダウンロードを停止(&S)
StopDownload=ダウンロードを中断してもよろしいですか？
ErrorDownloadAborted=ダウンロードが中止されました
ErrorDownloadFailed=ダウンロードに失敗しました: %1 %2
ErrorDownloadSizeFailed=サイズの取得に失敗しました: %1 %2
ErrorProgress=無効な進行状況: %2 中 %1
ErrorFileSize=無効なファイルサイズ: 期待値 %1、実際 %2

; *** TExtractionWizardPage ウィザードページと ExtractArchive
ExtractingLabel=ファイルを展開中...
ButtonStopExtraction=展開を停止(&S)
StopExtraction=展開を中断してもよろしいですか？
ErrorExtractionAborted=展開が中断されました
ErrorExtractionFailed=展開に失敗しました: %1

; *** アーカイブ展開失敗の詳細
ArchiveIncorrectPassword=パスワードが正しくありません
ArchiveIsCorrupted=アーカイブが破損しています
ArchiveUnsupportedFormat=このアーカイブ形式はサポートされていません

; *** 「インストールの準備」ウィザードページ
WizardPreparing=インストールの準備
PreparingDesc=コンピュータへの [name] のインストールを準備しています。
PreviousInstallNotCompleted=プログラムの以前のインストール/削除が完了していません。%n%nインストールを完了するには、システムを再起動する必要があります。%n%nシステムの再起動後、[name] のセットアップを再度実行してください。
CannotContinue=セットアップを続行できません。終了するには「キャンセル」を選択してください。
ApplicationsFound=以下のアプリケーションが、セットアップで更新する必要があるファイルを使用しています。%n%nこれらのアプリケーションを自動的に閉じることを許可することをお勧めします。
ApplicationsFound2=以下のアプリケーションが、セットアップで更新する必要があるファイルを使用しています。%n%nこれらのアプリケーションを自動的に閉じることを許可することをお勧めします。%n%nセットアップの完了後、アプリケーションの再起動を試みます。
CloseApplications=アプリケーションを自動的に閉じる(&A)
DontCloseApplications=アプリケーションを閉じない(&D)
ErrorCloseApplications=セットアップはすべてのアプリケーションを自動的に閉じることができませんでした。%n%n続行する前に、セットアップ中に更新する必要があるファイルを使用しているすべてのアプリケーションを閉じることをお勧めします。
PrepareToInstallNeedsRestart=セットアップはコンピュータを再起動する必要があります。システムの再起動後に [name] のインストールを完了するには、セットアップを再度実行してください。%n%nシステムを再起動しますか？

; *** 「インストール中」ウィザードページ
WizardInstalling=インストール中
InstallingLabel=コンピュータへの [name] のインストールが完了するまでお待ちください。

; *** 「セットアップ完了」ウィザードページ
FinishedHeadingLabel=[name] のインストールが完了しました
FinishedLabelNoIcons=[name] のインストールが完了しました。
FinishedLabel=[name] のインストールが完了しました。%n%nインストールされたショートカットを選択してアプリケーションを実行できます。
ClickFinish=セットアップを終了するには「完了」を選択してください。
FinishedRestartLabel=[name] のインストールを完了するには、システムを再起動する必要があります。%n%nシステムを再起動しますか？
FinishedRestartMessage=[name] のインストールを完了するには、システムを再起動する必要があります。%n%nシステムを再起動しますか？
ShowReadmeCheck=はい、今すぐ README ファイルを表示する
YesRadio=はい、今すぐシステムを再起動する(&Y)
NoRadio=いいえ、後でシステムを再起動する(&N)
; 例えば「Run MyProg.exe」として使用されます
RunEntryExec=%1 を実行
; 例えば「View Readme.txt」として使用されます
RunEntryShellExec=%1 を表示

; *** 「セットアップに次のディスクが必要」関連
ChangeDiskTitle=セットアップには次のディスクが必要です
SelectDiskLabel2=ディスク %1 を挿入し、「OK」を選択してください。%n%nこのディスクのファイルが以下に表示されているものとは異なるフォルダーにある場合は、正しいパスを入力するか「参照」を選択してください。
PathLabel=パス(&P):
FileNotInDir2=ファイル "%1" が "%2" に見つかりませんでした。%n%n正しいディスクを挿入するか、別のフォルダーを選択してください。
SelectDirectoryLabel=次のディスクの場所を指定してください。

; *** インストール段階のメッセージ
SetupAborted=セットアップが完了しませんでした。%n%n問題を修正して、セットアップを再度実行してください。
AbortRetryIgnoreSelectAction=操作を選択
AbortRetryIgnoreRetry=再試行(&R)
AbortRetryIgnoreIgnore=このエラーを無視して続行(&I)
AbortRetryIgnoreCancel=セットアップをキャンセル
RetryCancelSelectAction=操作を選択
RetryCancelRetry=再試行(&R)
RetryCancelCancel=キャンセル

; *** インストール状態メッセージ
StatusClosingApplications=アプリケーションを閉じています...
StatusCreateDirs=フォルダーを作成しています...
StatusExtractFiles=ファイルを展開しています...
StatusDownloadFiles=ファイルをダウンロードしています...
StatusCreateIcons=ショートカットを作成しています...
StatusCreateIniEntries=INI ファイルのエントリを作成しています...
StatusCreateRegistryEntries=レジストリエントリを作成しています...
StatusRegisterFiles=ファイルを登録しています...
StatusSavingUninstall=アンインストール情報を保存しています...
StatusRunProgram=セットアップを終了しています...
StatusRestartingApplications=アプリケーションを再起動しています...
StatusRollback=変更をロールバックしています...

; *** その他エラー
ErrorInternal2=内部エラー: %1
ErrorFunctionFailedNoCode=%1 に失敗しました
ErrorFunctionFailed=%1 に失敗しました。コード %2
ErrorFunctionFailedWithMessage=%1 に失敗しました。コード %2。%n%3
ErrorExecutingProgram=ファイルを実行できません:%n%1

; *** レジストリエラー
ErrorRegOpenKey=レジストリキーのオープンエラー:%n%1\%2
ErrorRegCreateKey=レジストリキーの作成エラー:%n%1\%2
ErrorRegWriteKey=レジストリキーの書き込みエラー:%n%1\%2

; *** INI エラー
ErrorIniEntry=ファイル "%1" への INI エントリの作成エラー。

; *** ファイルコピーエラー
FileAbortRetryIgnoreSkipNotRecommended=このファイルをスキップ(&S) (推奨されません)
FileAbortRetryIgnoreIgnoreNotRecommended=このエラーを無視して続行(&I) (推奨されません)
SourceIsCorrupted=ソースファイルが破損しています
SourceDoesntExist=ソースファイル "%1" が存在しません
SourceVerificationFailed=ソースファイルの検証に失敗しました: %1
VerificationSignatureDoesntExist=署名ファイル "%1" が利用できません
VerificationSignatureInvalid=署名ファイル "%1" が無効です
VerificationKeyNotFound=署名ファイル "%1" は不明なキーを使用しています
VerificationFileNameIncorrect=ファイル名が正しくありません
VerificationFileTagIncorrect=ファイルタグが正しくありません
VerificationFileSizeIncorrect=ファイルサイズが正しくありません
VerificationFileHashIncorrect=ファイルハッシュが正しくありません
ExistingFileReadOnly2=既存のファイルは読み取り専用としてマークされているため、置き換えることができません。
ExistingFileReadOnlyRetry=読み取り専用属性を削除して再試行(&R)
ExistingFileReadOnlyKeepExisting=既存のファイルを保持(&K)
ErrorReadingExistingDest=既存のファイルの読み取り中にエラーが発生しました:
FileExistsSelectAction=操作を選択
FileExists2=ファイルは既に存在します。
FileExistsOverwriteExisting=既存のファイルを上書き(&O)
FileExistsKeepExisting=既存のファイルを保持(&K)
FileExistsOverwriteOrKeepAll=以降の競合にもこの操作を適用(&A)
ExistingFileNewerSelectAction=操作を選択
ExistingFileNewer2=既存のファイルは、インストールしようとしているファイルよりも新しいです。
ExistingFileNewerOverwriteExisting=既存のファイルを上書き(&O)
ExistingFileNewerKeepExisting=既存のファイルを保持(&K) (推奨)
ExistingFileNewerOverwriteOrKeepAll=以降の競合にもこの操作を適用(&A)
ErrorChangingAttr=既存のファイルの属性を変更しようとしてエラーが発生しました:
ErrorCreatingTemp=インストール先フォルダーにファイルを作成しようとしてエラーが発生しました:
ErrorReadingSource=ソースファイルの読み取り中にエラーが発生しました:
ErrorCopying=ファイルのコピー中にエラーが発生しました:
ErrorDownloading=ファイルのダウンロード中にエラーが発生しました:
ErrorExtracting=アーカイブの展開中にエラーが発生しました:
ErrorReplacingExistingFile=既存のファイルの置き換え中にエラーが発生しました:
ErrorRestartReplace=再起動時の置き換えエラー:
ErrorRenamingTemp=インストール先フォルダー内のファイルの名前変更中にエラーが発生しました:
ErrorRegisterServer=DLL/OCX を登録できません: %1
ErrorRegSvr32Failed=RegSvr32 が終了コード %1 で失敗しました
ErrorRegisterTypeLib=タイプライブラリを登録できません: %1

; *** アンインストール表示名のマーク
; 例えば「My Program (32-bit)」として使用されます
UninstallDisplayNameMark=%1 (%2)
; 例えば「My Program (32-bit, All users)」として使用されます
UninstallDisplayNameMarks=%1 (%2, %3)
UninstallDisplayNameMark32Bit=32-bit
UninstallDisplayNameMark64Bit=64-bit
UninstallDisplayNameMarkAllUsers=すべてのユーザー
UninstallDisplayNameMarkCurrentUser=現在のユーザー

; *** インストール後のエラー
ErrorOpeningReadme=README ファイルを開く際にエラーが発生しました。
ErrorRestartingComputer=コンピュータを再起動できません。手動で再起動してください。

; *** アンインストーラメッセージ
UninstallNotFound=ファイル "%1" が存在しません。%n%nアンインストールできません。
UninstallOpenError=ファイル "%1" を開けません。%n%nアンインストールできません
UninstallUnsupportedVer=アンインストールログファイル "%1" は、このバージョンのアンインストーラでは認識されない形式です。%n%nアンインストールできません
UninstallUnknownEntry=アンインストールログに不明なエントリ (%1) が見つかりました
ConfirmUninstall=%1 とそのすべてのコンポーネントを完全に削除しますか？
UninstallOnlyOnWin64=このアプリケーションは 64-bit Windows でのみアンインストールできます。
OnlyAdminCanUninstall=このアプリケーションは管理者権限を持つユーザーのみがアンインストールできます。
UninstallStatusLabel=%1 がコンピュータから削除されるまでお待ちください。
UninstalledAll=%1 のアンインストールが完了しました。
UninstalledMost=%1 のアンインストールが完了しました。%n%n一部の項目は削除できませんでした。%n%n手動で削除する必要があります。
UninstalledAndNeedsRestart=%1 のアンインストールを完了するには、システムを再起動する必要があります。%n%nシステムを再起動しますか？
UninstallDataCorrupted=ファイル "%1" が破損しています。アンインストールできません

; *** アンインストール段階のメッセージ
ConfirmDeleteSharedFileTitle=共有ファイルを削除しますか？
ConfirmDeleteSharedFile2=システムは、以下の共有ファイルがどのプログラムからも使用されていないことを示しています。%nこの共有ファイルを削除しますか？%nもし何らかのプログラムがこのファイルを使用している場合、正しく動作しなくなる可能性があります。%n確信がない場合は「いいえ」を選択してください。%nファイルをシステムに残しても害はありません。
SharedFileNameLabel=ファイル名:
SharedFileLocationLabel=場所:
WizardUninstalling=アンインストール状態
StatusUninstalling=%1 をアンインストールしています...

; *** シャットダウンブロックの理由
ShutdownBlockReasonInstallingApp=%1 をインストールしています。
ShutdownBlockReasonUninstallingApp=%1 をアンインストールしています。

; 以下のカスタムメッセージはセットアップ自体では使用されませんが、
; スクリプトで使用する場合は翻訳することをお勧めします。

[CustomMessages]

NameAndVersion=%1 バージョン %2
AdditionalIcons=追加のショートカット:
CreateDesktopIcon=デスクトップにショートカットを作成(&D)
CreateQuickLaunchIcon=クイック起動バーにショートカットを作成(&Q)
ProgramOnTheWeb=%1 Web サイト
UninstallProgram=%1 をアンインストール
LaunchProgram=%1 を起動
AssocFileExtension=拡張子 %2 のファイルを %1 に関連付け(&A)
AssocingFileExtension=拡張子 %2 のファイルを %1 に関連付けています...
AutoStartProgramGroupDescription=スタートアップ:
AutoStartProgram=%1 を自動的に起動
AddonHostProgramNotFound=選択したフォルダーに %1 が見つかりませんでした。%n%n続行しますか？
