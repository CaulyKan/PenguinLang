grammar PenguinLang;

primaryExpression:
	Constant
	| StringLiteral+
	| boolLiteral
	| voidLiteral
	| identifierWithGeneric
	| lambdaFunctionExpression
	| '(' expression ')';

identifierWithGeneric: identifier genericArguments?;

postfixExpression:
	primaryExpression
	//	| slicingExpression
	| newExpression
	| spawnExpression
	| waitExpression
	| functionCallExpression
	| memberAccessExpression;

//slicingExpression: primaryExpression '[' expression ']';

spawnExpression: 'async' expression;

waitExpression: 'wait' expression?;

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

bitwiseAndExpression:
	equalityExpression ('&' equalityExpression)*;

bitwiseXorExpression:
	bitwiseAndExpression ('^' bitwiseAndExpression)*;

bitwiseOrExpression:
	bitwiseXorExpression ('|' bitwiseXorExpression)*;

logicalAndExpression:
	bitwiseOrExpression ('&&' bitwiseOrExpression)*;

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

typeReferenceDeclaration: 'type' identifier '=' typeSpecifier;

declarationWithoutInitializer: identifier ':' typeSpecifier;

declaration: identifier (':' typeSpecifier)? ( '=' expression)?;

classDeclaration:
	identifier (':' typeMutabilitySpecifier? typeSpecifier)? (
		'=' expression
	)?;

letKeyword: 'let';

storageClassSpecifier: 'extern';

typeMutabilitySpecifier: 'mut' | '!mut' | 'auto';

typeSpecifier:
	typeMutabilitySpecifier? typeSpecifierWithoutIterable iterableType?;

typeSpecifierWithoutIterable:
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
	| 'Self'
	| (('fun' | 'async_fun') genericArguments)
	| (
		identifierWithDotsAndGenericArguments (
			'.' identifierWithDotsAndGenericArguments
		)*
	);

iterableType: '[]';

identifierWithDots: identifier ('.' identifier)*;

identifierWithDotsAndGenericArguments:
	identifierWithDots genericArguments?;

genericArguments:
	'<' typeSpecifier (',' typeSpecifier)* (
		',' variadicGenericArguments
	)? '>';

variadicGenericArguments: '...';

genericDefinitions: '<' identifier (',' identifier)* '>';

whereClause: identifier ':' typeSpecifier;

whereDefinition: 'where' (whereClause (',' whereClause)*);

interfaceDefinition:
	'interface' identifier genericDefinitions? '{' (
		(declaration ';')
		| functionDefinition
		| interfaceImplementation
		| eventDefinition
	)* '}';

interfaceImplementation:
	'impl' typeSpecifier (whereDefinition)? (
		';'
		| ( '{' (functionDefinition)* '}')
	);

interfaceForImplementation:
	'impl' typeSpecifier 'for' typeSpecifier (whereDefinition)? (
		';'
		| ( '{' (functionDefinition)* '}')
	);

classDefinition:
	'class' identifier genericDefinitions? '{' (
		(classDeclaration ';')
		| functionDefinition
		| interfaceImplementation
		| eventDefinition
		| onRoutine
	)* '}';

enumDefinition:
	'enum' identifier genericDefinitions? '{' (
		enumDeclaration
		| functionDefinition
		| interfaceImplementation
		| eventDefinition
		| onRoutine
	)* '}';

enumDeclaration: identifier (':' typeSpecifier)? ';';

nestedParenthesesBlock: (
		~('(' | ')')
		| '(' nestedParenthesesBlock ')'
	)*;

//typeName: specifierQualifierList identifier?;

codeBlock: '{' codeBlockItem* '}';

codeBlockItem:
	statement
	| (letKeyword declaration ';')
	| (typeReferenceDeclaration ';');

statement:
	codeBlock
	| expressionStatement
	| ifStatement
	| whileStatement
	| forStatement
	| assignmentStatement
	| jumpStatement
	| returnStatement
	| yieldStatement
	| signalStatement
	| emitEventStatement
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
	'for' '(' letKeyword declaration 'in' expression ')' statement;

jumpStatement: jumpKeyword ';';

jumpKeyword: 'continue' | 'break';

returnStatement: returnKeyword expression? ';';

returnKeyword:
	'return'
	| '__yield_not_finished_return'
	| '__yield_finished_return'
	| '__blocked_return';

yieldStatement: 'yield' expression? ';';

signalStatement: '__signal' expression;

emitEventStatement: 'emit' expression '(' expression? ')' ';';

compilationUnit: namespaceDeclaration* EOF;

namespaceDeclaration:
	(letKeyword declaration ';')
	| typeReferenceDeclaration
	| namespaceDefinition
	| initialRoutine
	| functionDefinition
	| classDefinition
	| enumDefinition
	| interfaceDefinition
	| interfaceForImplementation
	| eventDefinition
	| onRoutine
	| ';';

thisParameter: 'mut'? 'this';
parameterList: (thisParameter | declaration)? (',' declaration)* ','?;

functionSpecifier:
	'pure'
	| '!pure'
	| 'extern'
	| 'async'
	| '!async';

lambdaFunctionExpression:
	('fun' | 'async_fun') ('(' parameterList ')')? (
		'->' typeSpecifier
	)? codeBlock;

functionDefinition:
	functionSpecifier* 'fun' (identifier | 'new') (
		'(' parameterList ')'
	)? ('->' typeSpecifier)? (codeBlock | ';');

eventDefinition: 'event' identifier (':' typeSpecifier)? ';';

initialRoutine: 'initial' identifier? codeBlock;

onRoutine:
	'on' expression ('(' declarationWithoutInitializer? ')')? codeBlock;

namespaceDefinition:
	'namespace' identifier '{' namespaceDeclaration* '}';

identifier: Identifier | 'this';

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

voidLiteral: 'void';

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