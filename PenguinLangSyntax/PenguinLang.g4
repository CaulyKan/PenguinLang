grammar PenguinLang;

primaryExpression:
	Constant
	| StringLiteral+
	| boolLiteral
	| identifierWithGeneric
	| '(' expression ')';

identifierWithGeneric: identifier genericArguments?;

postfixExpression:
	primaryExpression
	//	| slicingExpression
	| newExpression
	| functionCallExpression
	| memberAccessExpression;

//slicingExpression: primaryExpression '[' expression ']';

newExpression:
	'new' typeSpecifier '(' expression? (',' expression)* ')';

functionCallExpression:
	(memberAccessExpression | primaryExpression) '(' expression? (
		',' expression
	)* ')';

memberAccessExpression:
	primaryExpression ('.' identifierWithGeneric)+;

unaryExpression:
	postfixExpression
	| unaryOperator postfixExpression;

unaryOperator: '&' | '*' | '+' | '-' | '~' | '!';

castExpression:
	unaryExpression 'as' typeSpecifier
	| unaryExpression;

multiplicativeExpression:
	castExpression (multiplicativeOperator castExpression)*;

multiplicativeOperator: '*' | '/' | '%';

additiveExpression:
	multiplicativeExpression (
		additiveOperator multiplicativeExpression
	)*;

additiveOperator: '+' | '-';

shiftExpression:
	additiveExpression (shiftOperator additiveExpression)*;

shiftOperator: '<<' | rightShift;
rightShift:
	first = '>' second = '>' {$first.index + 1 == $second.index}?; // Nothing between the tokens?

relationalExpression:
	shiftExpression (relationalOperator shiftExpression)*;

relationalOperator: '<' | '>' | '<=' | '>=';

equalityExpression:
	relationalExpression (equalityOperator relationalExpression)?;

equalityOperator: '==' | '!=' | 'is';

andExpression: equalityExpression ('&' equalityExpression)*;

exclusiveOrExpression: andExpression ('^' andExpression)*;

inclusiveOrExpression:
	exclusiveOrExpression ('|' exclusiveOrExpression)*;

logicalAndExpression:
	inclusiveOrExpression ('&&' inclusiveOrExpression)*;

logicalOrExpression:
	logicalAndExpression ('||' logicalAndExpression)*;

conditionalExpression:
	logicalOrExpression '?' expression ':' conditionalExpression;

expression: logicalOrExpression;

assignmentOperator:
	'='
	| '*='
	| '/='
	| '%='
	| '+='
	| '-='
	| '<<='
	| '>>='
	| '&='
	| '^='
	| '|=';

constantExpression: conditionalExpression;

declaration:
	storageClassSpecifier* typeQualifier* declarationKeyword identifier (
		':' typeSpecifier
	)? ('=' expression)?;

declarationKeyword: 'var' | 'val';

typeQualifier: 'const';

storageClassSpecifier: 'extern';

typeSpecifier:
	'void'
	| 'u8'
	| 'u16'
	| 'u32'
	| 'u64'
	| 'i8'
	| 'i16'
	| 'i32'
	| 'i64'
	| 'float'
	| 'double'
	| 'string'
	| 'bool'
	| 'char'
	| identifierWithDots genericArguments?;

identifierWithDots: identifier ('.' identifier)*;

genericArguments: '<' typeSpecifier (',' typeSpecifier)* '>';

genericDefinitions: '<' identifier (',' identifier)* '>';

interfaceDefinition:
	'interface' identifier genericDefinitions? '{' (
		functionDefinition
		| interfaceImplementation
	)* '}';

interfaceImplementation:
	'impl' typeSpecifier (';' | ( '{' (functionDefinition)* '}'));

classDefinition:
	'class' identifier genericDefinitions? '{' (
		classDeclaration
		| functionDefinition
		| interfaceImplementation
	)* '}';

classDeclaration:
	declarationKeyword identifier ':' typeSpecifier (
		'=' expression
	)? ';';

enumDefinition:
	'enum' identifier genericDefinitions? '{' (
		enumDeclaration
		| functionDefinition
	)* '}';

enumDeclaration: identifier (':' typeSpecifier)? ';';

nestedParenthesesBlock: (
		~('(' | ')')
		| '(' nestedParenthesesBlock ')'
	)*;

//typeName: specifierQualifierList identifier?;

codeBlock: '{' codeBlockItem* '}';

codeBlockItem: statement | (declaration ';');

statement:
	codeBlock
	| expressionStatement
	| ifStatement
	| whileStatement
	| forStatement
	| assignmentStatement
	| jumpStatement
	| returnStatement
	| ';';

identifierOrMemberAccess: identifier | memberAccessExpression;

assignmentStatement:
	identifierOrMemberAccess assignmentOperator expression ';';

expressionStatement: expression ';';

ifStatement:
	'if' '(' expression ')' statement ('else' statement)?;
//| 'switch' '(' expression ')' statement;

whileStatement: 'while' '(' expression ')' statement;

forStatement:
	'for' '(' declaration 'in' expression ')' statement;

jumpStatement: jumpKeyword ';';

jumpKeyword: 'continue' | 'break';

returnStatement: 'return' expression? ';';

compilationUnit: namespaceDeclaration* EOF;

namespaceDeclaration:
	declaration
	| namespaceDefinition
	| initialRoutine
	| functionDefinition
	| classDefinition
	| enumDefinition
	| interfaceDefinition
	| ';';

parameterList: declaration? (',' declaration)* ','?;

functionSpecifier: 'pure' | '!pure' | 'extern';

functionDefinition:
	functionSpecifier* 'fun' (identifier | 'new') '(' parameterList ')' (
		'->' typeSpecifier
	)? (codeBlock | ';');

initialRoutine: 'initial' identifier? codeBlock;

namespaceDefinition:
	'namespace' identifier '{' namespaceDeclaration* '}';

identifier: Identifier;

Identifier: IdentifierNondigit (IdentifierNondigit | Digit)*;

fragment IdentifierNondigit: Nondigit;
//| UniversalCharacterName | // other implementation-defined characters...;

fragment Nondigit: [a-zA-Z_];

fragment Digit: [0-9];

fragment UniversalCharacterName:
	'\\u' HexQuad
	| '\\U' HexQuad HexQuad;

fragment HexQuad:
	HexadecimalDigit HexadecimalDigit HexadecimalDigit HexadecimalDigit;

Constant:
	IntegerConstant
	| FloatingConstant
	//|   EnumerationConstant
	| CharacterConstant;

fragment IntegerConstant:
	DecimalConstant IntegerSuffix?
	| OctalConstant IntegerSuffix?
	| HexadecimalConstant IntegerSuffix?
	| BinaryConstant;

fragment BinaryConstant: '0' [bB] [0-1]+;

fragment DecimalConstant: NonzeroDigit Digit*;

fragment OctalConstant: '0' OctalDigit*;

fragment HexadecimalConstant:
	HexadecimalPrefix HexadecimalDigit+;

fragment HexadecimalPrefix: '0' [xX];

fragment NonzeroDigit: [1-9];

fragment OctalDigit: [0-7];

fragment HexadecimalDigit: [0-9a-fA-F];

fragment IntegerSuffix:
	UnsignedSuffix LongSuffix?
	| UnsignedSuffix LongLongSuffix
	| LongSuffix UnsignedSuffix?
	| LongLongSuffix UnsignedSuffix?;

fragment UnsignedSuffix: [uU];

fragment LongSuffix: [lL];

fragment LongLongSuffix: 'll' | 'LL';

fragment FloatingConstant:
	DecimalFloatingConstant
	| HexadecimalFloatingConstant;

fragment DecimalFloatingConstant:
	FractionalConstant ExponentPart? FloatingSuffix?
	| DigitSequence ExponentPart FloatingSuffix?;

fragment HexadecimalFloatingConstant:
	HexadecimalPrefix (
		HexadecimalFractionalConstant
		| HexadecimalDigitSequence
	) BinaryExponentPart FloatingSuffix?;

fragment FractionalConstant:
	DigitSequence? '.' DigitSequence
	| DigitSequence '.';

fragment ExponentPart: [eE] Sign? DigitSequence;

fragment Sign: [+-];

DigitSequence: Digit+;

fragment HexadecimalFractionalConstant:
	HexadecimalDigitSequence? '.' HexadecimalDigitSequence
	| HexadecimalDigitSequence '.';

fragment BinaryExponentPart: [pP] Sign? DigitSequence;

fragment HexadecimalDigitSequence: HexadecimalDigit+;

fragment FloatingSuffix: [flFL];

fragment CharacterConstant:
	'\'' CCharSequence '\''
	| 'L\'' CCharSequence '\''
	| 'u\'' CCharSequence '\''
	| 'U\'' CCharSequence '\'';

fragment CCharSequence: CChar+;

fragment CChar: ~['\\\r\n] | EscapeSequence;

fragment EscapeSequence:
	SimpleEscapeSequence
	| OctalEscapeSequence
	| HexadecimalEscapeSequence
	| UniversalCharacterName;

fragment SimpleEscapeSequence: '\\' ['"?abfnrtv\\];

fragment OctalEscapeSequence:
	'\\' OctalDigit OctalDigit? OctalDigit?;

fragment HexadecimalEscapeSequence: '\\x' HexadecimalDigit+;

StringLiteral: EncodingPrefix? '"' SCharSequence? '"';

boolLiteral: 'true' | 'false';

fragment EncodingPrefix: 'u8' | 'u' | 'U' | 'L';

fragment SCharSequence: SChar+;

fragment SChar:
	~["\\\r\n]
	| EscapeSequence
	| '\\\n' // Added line
	| '\\\r\n'; // Added line

Whitespace: [ \t]+ -> channel(HIDDEN);

Newline: ('\r' '\n'? | '\n') -> channel(HIDDEN);

BlockComment: '/*' .*? '*/' -> channel(HIDDEN);

LineComment: '//' ~[\r\n]* -> channel(HIDDEN);